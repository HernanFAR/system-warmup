using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace SystemWarmup;
public static class SystemOperations
{
    // A deliberately conservative denylist; this tool must not be an unrestricted service killer.
    public static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase) {
        "WinDefend", "WdNisSvc", "MDCoreSvc", "LSM", "RpcSs", "DcomLaunch", "PlugPlay",
        "EventLog", "Power", "LanmanWorkstation", "Dhcp", "Dnscache", "Schedule",
        "ProfSvc", "Winmgmt", "W32Time", "BFE", "mpssvc", "CryptSvc", "SamSs",
        "TrustedInstaller", "gpsvc", "Appinfo", "KeyIso", "NlaSvc", "nsi"
    };
    public static async Task<(int Code, string Output)> RunAsync(string exe, params string[] args)
    {
        using var p = new Process { StartInfo = new ProcessStartInfo(exe) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        foreach (string arg in args) p.StartInfo.ArgumentList.Add(arg);
        p.Start();
        Task<string> a = p.StandardOutput.ReadToEndAsync();
        Task<string> b = p.StandardError.ReadToEndAsync();
        using var ct = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await p.WaitForExitAsync(ct.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch {} return (-1,"Command timeout"); }
        return (p.ExitCode, (await a) + Environment.NewLine + (await b));
    }
    public static async Task<List<ServiceItem>> DiscoverAsync()
    {
        const string cmd = "Get-CimInstance Win32_Service | Select-Object Name,DisplayName,State,StartMode | ConvertTo-Json -Compress -Depth 3";
        var r = await RunAsync("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", cmd);
        if (r.Code != 0) throw new InvalidOperationException(r.Output);
        using var json = JsonDocument.Parse(r.Output);
        var elements = json.RootElement.ValueKind == JsonValueKind.Array
            ? json.RootElement.EnumerateArray().ToArray() : new[] { json.RootElement };
        return elements.Select(x => new ServiceItem {
            Name = x.GetProperty("Name").GetString() ?? "",
            DisplayName = x.GetProperty("DisplayName").GetString() ?? "",
            State = x.GetProperty("State").GetString() ?? "",
            StartMode = x.GetProperty("StartMode").GetString() ?? ""
        }).OrderBy(x => x.DisplayName).ToList();
    }
    public static async Task<string> ApplyServiceAsync(ServiceItem item, DesiredState desired)
    {
        if (Protected.Contains(item.Name)) return $"PROTECTED {item.Name}: unchanged";
        if (desired == DesiredState.Ignore) return $"SKIP {item.Name}";
        bool running = item.State.Equals("Running", StringComparison.OrdinalIgnoreCase);
        if (desired == DesiredState.Stop && !running) return $"ALREADY STOPPED {item.Name}";
        if (desired == DesiredState.KeepRunning && running) return $"ALREADY RUNNING {item.Name}";
        var r = await RunAsync("sc.exe", desired == DesiredState.Stop ? "stop" : "start", item.Name);
        return $"{(r.Code == 0 ? "REQUESTED" : "FAILED")} {item.Name}: {r.Output.Trim()}";
    }
    public static async Task<string> ShutdownWslAsync()
    {
        var r = await RunAsync("wsl.exe", "--shutdown");
        return $"WSL shutdown {(r.Code == 0 ? "requested" : "failed")}: {r.Output.Trim()}";
    }
    public static string LaunchDocker()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe");
        if (!File.Exists(path)) return "Docker Desktop not found at default location";
        if (Process.GetProcessesByName("Docker Desktop").Length > 0) return "Docker Desktop already open";
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return "Docker Desktop launch requested";
    }
    public static IEnumerable<string> CloseOptionalProcesses(IEnumerable<string> names)
    {
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (new[] { "explorer", "msedge", "dwm", "System", "MsMpEng", "pwsh", "powershell" }
                .Contains(name, StringComparer.OrdinalIgnoreCase)) { yield return $"PROTECTED PROCESS {name}"; continue; }
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                        yield return $"CLOSE REQUESTED {name} ({process.Id})";
                    else yield return $"NO WINDOW / NOT CLOSED {name} ({process.Id})";
                }
            }
        }
    }
    public static async Task<string> SetAutorunAsync(bool enable)
    {
        const string task = "SystemWarmup";
        if (!enable)
        {
            var r = await RunAsync("schtasks.exe", "/Delete", "/TN", task, "/F");
            return r.Code == 0 ? "Autostart disabled" : r.Output;
        }
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable");
        if (Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publish a standalone EXE before enabling autostart.");
        // The Task Scheduler stores a stable executable path; the location must not be temporary.
        var command = ((char)34) + exe + ((char)34) + " --startup";
        var created = await RunAsync("schtasks.exe", "/Create", "/TN", task, "/SC", "ONLOGON",
            "/RL", "HIGHEST", "/TR", command, "/F");
        return created.Code == 0 ? "Autostart enabled for logon (high privileges)" : created.Output;
    }
}