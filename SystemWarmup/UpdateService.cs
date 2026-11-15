using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SystemWarmup;

public sealed record AvailableUpdate(string Tag, string Description, string ReleaseUrl,
    string DownloadUrl, string Sha256, string Repository);

public sealed record PreparedUpdate(AvailableUpdate Release, string Directory, string ApplicationRoot, string Script);

/// <summary>Only signed-by-transport GitHub release metadata and SHA-256-verified archives are admitted.</summary>
public sealed class UpdateService : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly Regex RepoPattern = new(@"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);
    public UpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SystemWarmup", "0.1"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<AvailableUpdate?> CheckAsync(UpdateOptions settings, Version current, CancellationToken ct = default)
    {
        string repo = (settings.Repository ?? "").Trim();
        if (!RepoPattern.IsMatch(repo)) throw new InvalidOperationException("Repositorio inválido. Usa propietario/repositorio.");
        if (settings.Channel != "Stable" && settings.Channel != "Prerelease")
            throw new InvalidOperationException("Canal desconocido. Selecciona Stable o Prerelease.");
        using var response = await _client.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=30", ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            if (settings.Channel == "Stable" && release.GetProperty("prerelease").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!TryVersion(tag, out var version) || version <= current) continue;
            if (!release.TryGetProperty("assets", out var assets)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != "SystemWarmup-win-x64.zip") continue;
                string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(digest[7..], @"^[0-9a-fA-F]{64}$"))
                    throw new InvalidDataException("El release no publica un digest SHA-256 verificable.");
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Origen del archivo de actualización inválido.");
                return new AvailableUpdate(tag,
                    release.TryGetProperty("body", out var body) ? (body.GetString() ?? "") : "",
                    release.GetProperty("html_url").GetString() ?? "", url, digest[7..].ToUpperInvariant(), repo);
            }
        }
        return null;
    }

    private static bool TryVersion(string tag, out Version version)
    {
        string candidate = tag.TrimStart('v', 'V').Split('-', '+')[0];
        if (Version.TryParse(candidate, out Version? parsed))
        {
            version = parsed; return true;
        }
        version = new Version(0, 0); return false;
    }

    public async Task<PreparedUpdate> PrepareAsync(AvailableUpdate release, IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        string folder = Path.Combine(Settings.Folder, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string zip = Path.Combine(folder, "update.zip");
        string extracted = Path.Combine(folder, "package");
        status?.Report("Descargando paquete...");
        using (var response = await _client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 150L * 1024 * 1024)
                throw new InvalidDataException("El paquete supera el tamaño máximo permitido (150 MiB).");
            await using var remote = await response.Content.ReadAsStreamAsync(ct);
            await using var local = File.Create(zip);
            byte[] buffer = new byte[65536];
            long total = 0;
            int n;
            while ((n = await remote.ReadAsync(buffer, ct)) > 0)
            {
                total += n;
                if (total > 150L * 1024 * 1024)
                    throw new InvalidDataException("Descarga excede 150 MiB.");
                await local.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        status?.Report("Verificando SHA-256...");
        await using (var input = File.OpenRead(zip))
        {
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 no coincide. Descarga rechazada sin modificar la aplicación.");
        }

        using (var archive = ZipFile.OpenRead(zip))
        {
            if (archive.Entries.Count > 2000 ||
                archive.Entries.Sum(x => x.Length) > 500L * 1024 * 1024)
                throw new InvalidDataException("El archivo contiene demasiados datos.");
            foreach (var entry in archive.Entries)
            {
                // Reject Unix symlinks and special files even though extraction normally runs on Windows.
                int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixType == 0xA000 || unixType == 0x6000 || unixType == 0x2000)
                    throw new InvalidDataException("El paquete contiene enlaces o archivos especiales.");
            }
            status?.Report("Preparando contenido...");
            archive.ExtractToDirectory(extracted, overwriteFiles: false);
        }
        string exe = Path.Combine(extracted, "SystemWarmup.exe");
        if (!File.Exists(exe) || !File.Exists(Path.Combine(extracted, "SystemWarmup.dll")) ||
            !File.Exists(Path.Combine(extracted, "SystemWarmup.runtimeconfig.json")))
            throw new InvalidDataException("El paquete no contiene una aplicación System Warmup válida en su raíz.");
        string script = Path.Combine(folder, "apply-update.ps1");
        await File.WriteAllTextAsync(script, UpdateScript.Content, ct);
        return new PreparedUpdate(release, folder, extracted, script);
    }

    public void Dispose() => _client.Dispose();
}