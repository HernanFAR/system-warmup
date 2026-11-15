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
    string DownloadUrl, string Sha256, string Repository, string Channel, long RunId = 0, string Commit = "", int Attempt = 1);

public sealed record PreparedUpdate(AvailableUpdate Release, string Directory, string ApplicationRoot, string Script);

/// <summary>GitHub release and authenticated Actions artifact updater. Tokens never enter settings.json.</summary>
public sealed class UpdateService : IDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly Regex RepoPattern = new(@"^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);
    public UpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SystemWarmup", "0.2"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<AvailableUpdate?> CheckAsync(UpdateOptions settings, Version current,
        InstalledUpdate? installed = null, CancellationToken ct = default)
    {
        string repo = (settings.Repository ?? "").Trim();
        if (!RepoPattern.IsMatch(repo)) throw new InvalidOperationException("Repositorio inválido. Usa propietario/repositorio.");
        string channel = settings.Channel is "Stable" ? "Release" :
            settings.Channel is "Prerelease" ? "Preview" : settings.Channel;
        if (channel is not ("Release" or "Preview" or "Build"))
            throw new InvalidOperationException("Canal desconocido. Usa Release, Preview o Build.");
        return channel == "Build"
            ? await CheckBuildAsync(repo, settings.PullRequest, installed, ct)
            : await CheckReleaseAsync(repo, channel, current, installed, ct);
    }

    private async Task<AvailableUpdate?> CheckReleaseAsync(string repo, string channel, Version current,
        InstalledUpdate? installed, CancellationToken ct)
    {
        using var response = await _client.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=100", ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var candidates = document.RootElement.EnumerateArray()
            .Where(x => !x.GetProperty("draft").GetBoolean() &&
                        x.GetProperty("prerelease").GetBoolean() == (channel == "Preview"))
            .Select(x => new { Element = x, Tag = x.GetProperty("tag_name").GetString() ?? "" })
            .Where(x => TryVersion(x.Tag, out _))
            .OrderByDescending(x => ParseVersion(x.Tag))
            .ToList();
        foreach (var release in candidates)
        {
            Version version = ParseVersion(release.Tag);
            if (version <= current && !(installed is { } prior &&
                prior.Channel == channel && prior.Repository.Equals(repo, StringComparison.OrdinalIgnoreCase) &&
                !prior.Identity.Equals(release.Tag, StringComparison.OrdinalIgnoreCase))) continue;
            if (installed is { } existing && existing.Channel == channel &&
                existing.Repository.Equals(repo, StringComparison.OrdinalIgnoreCase) &&
                existing.Identity == release.Tag) return null;

            foreach (var asset in release.Element.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != "SystemWarmup-win-x64.zip") continue;
                string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(digest[7..], @"^[0-9a-fA-F]{64}$"))
                    throw new InvalidDataException("Release sin digest SHA-256 verificable.");
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                    uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("URL de release inválida.");
                return new AvailableUpdate(release.Tag,
                    release.Element.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                    release.Element.GetProperty("html_url").GetString() ?? "",
                    url, digest[7..].ToUpperInvariant(), repo, channel);
            }
        }
        return null;
    }

    private async Task<AvailableUpdate?> CheckBuildAsync(string repo, int pr, InstalledUpdate? installed, CancellationToken ct)
    {
        if (pr <= 0) throw new InvalidOperationException("Build requiere un número de PR positivo.");
        await SetBuildAuthenticationAsync(ct);
        using var prDoc = await GetJsonAsync($"https://api.github.com/repos/{repo}/pulls/{pr}", ct);
        var pull = prDoc.RootElement;
        if (pull.GetProperty("state").GetString() != "open")
            throw new InvalidOperationException($"El PR #{pr} no está abierto.");
        string head = pull.GetProperty("head").GetProperty("sha").GetString() ?? "";
        string headBranch = pull.GetProperty("head").GetProperty("ref").GetString() ?? "";
        if (head.Length != 40) throw new InvalidDataException("El PR carece de un commit identificable.");

        using var runsDoc = await GetJsonAsync($"https://api.github.com/repos/{repo}/actions/runs?per_page=100", ct);
        var runs = runsDoc.RootElement.GetProperty("workflow_runs").EnumerateArray()
            .Where(x => x.GetProperty("event").GetString() == "pull_request" &&
                        x.GetProperty("head_sha").GetString() == head &&
                        x.GetProperty("pull_requests").EnumerateArray().Any(p => p.GetProperty("number").GetInt32() == pr) &&
                        x.GetProperty("path").GetString()!.EndsWith("/desktop-build.yml", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.GetProperty("run_number").GetInt64())
            .ThenByDescending(x => x.GetProperty("run_attempt").GetInt32())
            .ToList();

        if (runs.Count == 0)
            throw new InvalidOperationException($"No se encontró ejecución de CI del commit actual del PR #{pr} entre las últimas 100 ejecuciones.");
        var run = runs[0];
        long runNumber = run.GetProperty("run_number").GetInt64();
        long runId = run.GetProperty("id").GetInt64();
        string identity = $"build-{pr}-{runNumber}";
        string status = run.GetProperty("status").GetString() ?? "";
        string conclusion = run.GetProperty("conclusion").ValueKind == JsonValueKind.Null ? "" :
            run.GetProperty("conclusion").GetString() ?? "";
        if (status != "completed" || conclusion != "success")
            throw new InvalidOperationException($"{identity} no está disponible: CI {status}/{conclusion}. No se instalará un build anterior.");
        if (installed is { } local && local.Channel == "Build" &&
            local.Repository.Equals(repo, StringComparison.OrdinalIgnoreCase) &&
            local.Identity == identity && local.Commit == head &&
            local.Attempt == run.GetProperty("run_attempt").GetInt32()) return null;

        using var artsDoc = await GetJsonAsync($"https://api.github.com/repos/{repo}/actions/runs/{runId}/artifacts?per_page=100", ct);
        var arts = artsDoc.RootElement.GetProperty("artifacts").EnumerateArray().ToList();
        var artifacts = arts.Where(x => x.GetProperty("name").GetString() == "SystemWarmup-win-x64").ToList();
        if (artifacts.Count == 0)
            throw new InvalidOperationException($"{identity} no tiene el artefacto SystemWarmup-win-x64.");
        var artifact = artifacts[0];
        if (artifact.GetProperty("expired").GetBoolean())
            throw new InvalidOperationException($"{identity}: el artefacto expiró y ya no se puede descargar.");
        string digest = artifact.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(digest[7..], @"^[0-9a-fA-F]{64}$"))
            throw new InvalidDataException($"{identity} no incluye digest SHA-256.");
        string download = artifact.GetProperty("archive_download_url").GetString() ?? "";
        if (!Uri.TryCreate(download, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.Host != "api.github.com" || !uri.AbsolutePath.StartsWith($"/repos/{repo}/actions/artifacts/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("URL del artefacto de Actions inválida.");
        return new AvailableUpdate(identity, $"PR #{pr} · {headBranch} · {head[..12]} · run #{runNumber}",
            run.GetProperty("html_url").GetString() ?? "",
            download, digest[7..].ToUpperInvariant(), repo, "Build", runId, head,
            run.GetProperty("run_attempt").GetInt32());
    }

    private async Task SetBuildAuthenticationAsync(CancellationToken ct)
    {
        string? token = Environment.GetEnvironmentVariable("GH_TOKEN") ??
                        Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo("gh", "auth token")
                    {
                        UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true
                    }
                };
                process.Start();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                token = (await process.StandardOutput.ReadToEndAsync(timeout.Token)).Trim();
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0) token = null;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { token = null; }
        }
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Canal Build requiere GH_TOKEN, GITHUB_TOKEN o 'gh auth login'.");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await _client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static bool TryVersion(string tag, out Version version)
    {
        string candidate = tag.TrimStart('v', 'V').Split('-', '+')[0];
        if (Version.TryParse(candidate, out Version? parsed)) { version = parsed; return true; }
        version = new Version(0, 0); return false;
    }
    private static Version ParseVersion(string tag) =>
        TryVersion(tag, out var version) ? version : new Version(0, 0);

    public async Task<PreparedUpdate> PrepareAsync(AvailableUpdate release, IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        string folder = Path.Combine(Settings.Folder, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string zip = Path.Combine(folder, "update.zip");
        string extracted = Path.Combine(folder, "package");
        if (release.Channel == "Build") await SetBuildAuthenticationAsync(ct);
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
        if (!File.Exists(exe) || new FileInfo(exe).Length < 100_000)
            throw new InvalidDataException("El paquete no contiene un ejecutable System Warmup válido en su raíz.");
        string script = Path.Combine(folder, "apply-update.ps1");
        await File.WriteAllTextAsync(script, UpdateScript.Content, ct);
        return new PreparedUpdate(release, folder, extracted, script);
    }

    public static InstalledUpdate? ReadInstalled()
    {
        string path = Path.Combine(Settings.Folder, "installed-update.json");
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<InstalledUpdate>(File.ReadAllText(path))
                : null;
        }
        catch { return null; }
    }

    public static string PrepareInstallMetadata(AvailableUpdate update, string folder)
    {
        string path = Path.Combine(folder, "installed-update.json");
        File.WriteAllText(path, JsonSerializer.Serialize(
            new InstalledUpdate(update.Channel, update.Repository, update.Tag, update.RunId, update.Commit, update.Attempt)));
        return path;
    }

    public void Dispose() => _client.Dispose();
}