using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SystemWarmup;
public enum DesiredState { Ignore, KeepRunning, Stop }
public sealed class Profile
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, DesiredState> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShutdownWsl { get; set; }
    public bool LaunchDockerDesktop { get; set; }
    public List<string> CloseProcesses { get; set; } = [];
}
public sealed class UpdateOptions
{
    public string Repository { get; set; } = "HernanFAR/system-warmup";
    public string Channel { get; set; } = "Release";
    public int PullRequest { get; set; } = 1;
    public bool CheckOnLaunch { get; set; } = true;
}
public sealed record InstalledUpdate(string Channel, string Repository, string Identity, long RunId, string Commit);
public sealed class Settings
{
    public string SelectedProfile { get; set; } = "Benchmark";
    public bool ApplyAtLogin { get; set; }
    public string Theme { get; set; } = "Dark";
    public UpdateOptions Updates { get; set; } = new();
    public List<string> BaselineRunningServices { get; set; } = [];
    public DateTimeOffset? BaselineCapturedAt { get; set; }
    public List<Profile> Profiles { get; set; } = [];
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SystemWarmup");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })
                    ?? Defaults();
        }
        catch { /* Invalid configuration should not silently trigger service changes. */ }
        return Defaults();
    }
    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, FilePath, true);
    }
    public static Settings Defaults()
    {
        string[] optional = [
            "postgresql-x64-15", "postgresql-x64-16", "TeamViewer",
            "Apple Mobile Device Service", "Bonjour Service", "JetBrainsEtwHost.16",
            "IpOverUsbSvc", "SQLTELEMETRY", "SQLWriter"
        ];
        var benchmark = new Profile { Name = "Benchmark", Description = "Recursos libres para inferencia; WSL y Docker detenidos.",
            ShutdownWsl = true,
            CloseProcesses = ["Docker Desktop", "OneDrive", "PhoneExperienceHost", "CrossDeviceService", "CrossDeviceResume", "WidgetBoard", "MicrosoftStartFeedProvider"] };
        foreach (var n in optional.Concat(["docker", "com.docker.service"])) benchmark.Services[n] = DesiredState.Stop;

        var developer = new Profile { Name = "Minimal Developer",
            Description = "Docker Desktop, WSL y servicios mínimos; bases de datos nativas opcionales.",
            LaunchDockerDesktop = true };
        foreach (var n in optional) developer.Services[n] = DesiredState.Stop;
        foreach (var n in new[] { "WSLService", "com.docker.service" }) developer.Services[n] = DesiredState.KeepRunning;
        developer.CloseProcesses = ["WidgetBoard", "MicrosoftStartFeedProvider"];
        return new Settings { Profiles = [benchmark, developer] };
    }
}
public sealed class ServiceItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string State { get; set; } = "";
    public string StartMode { get; set; } = "";
    public bool Baseline { get; set; }
    public bool Protected { get; set; }
    private DesiredState _desired;
    public DesiredState Desired
    {
        get => _desired;
        set { _desired = value; PropertyChanged?.Invoke(this, new(nameof(Desired))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}