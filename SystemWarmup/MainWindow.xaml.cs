using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SystemWarmup;
public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly ObservableCollection<ServiceItem> _items = [];
    private bool _loading = true;
    private readonly ThemeManager _theme = new();
    private bool _busy;
    private string _previousProfile = "";
    public DesiredState[] ActionOptions { get; } = Enum.GetValues<DesiredState>();
    public ICollectionView FilteredServices { get; }

    public MainWindow()
    {
        FilteredServices = CollectionViewSource.GetDefaultView(_items);
        FilteredServices.Filter = o => o is ServiceItem s &&
            (string.IsNullOrWhiteSpace(SearchBox?.Text) ||
             s.Name.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) ||
             s.DisplayName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
        InitializeComponent();
        DataContext = this;
        ApplyTheme();
        UpdateInstalledLabel();
        ProfilePicker.ItemsSource = _settings.Profiles;
        ProfilePicker.SelectedItem = _settings.Profiles.FirstOrDefault(p => p.Name == _settings.SelectedProfile)
            ?? _settings.Profiles.FirstOrDefault();
        _previousProfile = Current?.Name ?? "";
        ApplyAtLogin.IsChecked = _settings.ApplyAtLogin;
        ShutdownWslBox.IsChecked = Current?.ShutdownWsl ?? false;
        LaunchDockerBox.IsChecked = Current?.LaunchDockerDesktop ?? false;
        _loading = false;
        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            if (_settings.Updates?.CheckOnLaunch == true)
                _ = CheckUpdateOnLaunchAsync();
            if (Environment.GetCommandLineArgs().Contains("--startup") && _settings.ApplyAtLogin)
                await ApplyAsync(fromStartup: true);
        };
    }
    private void ApplyTheme()
    {
        bool dark = !string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        _theme.Apply(dark);
        ThemeToggle.Content = dark ? "☀  Modo diurno" : "☾  Modo nocturno";
        ThemeToggle.ToolTip = dark ? "Cambiar al tema claro" : "Cambiar al tema oscuro";
    }
    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        _settings.Save();
        ApplyTheme();
    }
    private void UpdateInstalledLabel()
    {
        var installed = UpdateService.ReadInstalled();
        string assembly = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        UpdateStatusButton.Content = installed?.Identity ?? "v" + assembly;
        UpdateNotice.Text = "";
    }
    private async Task CheckUpdateOnLaunchAsync()
    {
        try
        {
            using var updater = new UpdateService();
            var info = await updater.CheckAsync(_settings.Updates,
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0),
                UpdateService.ReadInstalled());
            if (info is not null)
            {
                UpdateNotice.Text = "● " + info.Tag + " disponible";
                Log($"Nueva versión disponible: {info.Tag}. Abre Actualizaciones para revisar.");
            }
        }
        catch (Exception ex)
        {
            UpdateNotice.Text = "Build no disponible · revisar";
            UpdateNotice.ToolTip = ex.Message;
            Log("No se pudo consultar actualizaciones: " + ex.Message);
        }
    }
    private async void Updates_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UpdateWindow(_settings) { Owner = this };
        dialog.ShowDialog();
        UpdateInstalledLabel();
        if (_settings.Updates.CheckOnLaunch)
            await CheckUpdateOnLaunchAsync();
    }
    private Profile? Current => ProfilePicker.SelectedItem as Profile;

    private void Log(string text)
    {
        ActivityLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        ActivityLog.ScrollToEnd();
        try
        {
            System.IO.Directory.CreateDirectory(Settings.Folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Folder, "activity.log"),
                $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch { /* Logging failure must not affect services. */ }
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        ApplyButton.IsEnabled = false;
        FooterText.Text = "Consultando servicios de Windows…";
        try
        {
            var services = await SystemOperations.DiscoverAsync();
            _loading = true;
            _items.Clear();
            foreach (var s in services)
            {
                s.Protected = SystemOperations.Protected.Contains(s.Name);
                s.Baseline = _settings.BaselineRunningServices.Contains(s.Name, StringComparer.OrdinalIgnoreCase);
                if (Current != null && Current.Services.TryGetValue(s.Name, out var choice))
                    s.Desired = s.Protected ? DesiredState.Ignore : choice;
                _items.Add(s);
            }
            _loading = false;
            RunningValue.Text = services.Count(s => s.State.Equals("Running", StringComparison.OrdinalIgnoreCase)).ToString();
            MemoryValue.Text = AvailableMemoryGiB();
            BaselineText.Text = _settings.BaselineCapturedAt is { } date
                ? $"Captura: {date:dd/MM HH:mm}" : "Aún sin captura";
            RulesValue.Text = _items.Count(s => s.Desired != DesiredState.Ignore).ToString();
            ProfileDescription.Text = Current?.Description ?? "";
            FooterText.Text = $"{services.Count} servicios descubiertos · {services.Count(s => s.Protected)} protegidos";
            Log($"Inventario actualizado: {services.Count} servicios.");
            // First capture is descriptive, not a record of Windows' original state before starting the app.
            if (_settings.BaselineCapturedAt is null) CaptureBaseline();
        }
        catch (Exception ex) { _loading = false; Log("ERROR: " + ex.Message); MessageBox.Show(ex.Message, "No se pudo consultar servicios"); }
        finally { _busy = false; ApplyButton.IsEnabled = true; }
    }
    private static string AvailableMemoryGiB()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? $"{status.AvailablePhysical / (1024.0 * 1024 * 1024):F2} GB" : "N/D";
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private void CaptureBaseline()
    {
        _settings.BaselineRunningServices = _items
            .Where(s => s.State.Equals("Running", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Name).ToList();
        _settings.BaselineCapturedAt = DateTimeOffset.Now;
        foreach (var s in _items) s.Baseline = _settings.BaselineRunningServices.Contains(s.Name);
        BaselineText.Text = $"Captura: {DateTime.Now:dd/MM HH:mm}";
        _settings.Save();
        Log("Captura del estado actual guardada (no implica estado original de arranque).");
    }
    private void Capture_Click(object sender, RoutedEventArgs e) => CaptureBaseline();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Search_Changed(object sender, TextChangedEventArgs e) => FilteredServices?.Refresh();
    private void Rule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Current == null) return;
        SaveRules(_previousProfile);
        RulesValue.Text = _items.Count(s => s.Desired != DesiredState.Ignore).ToString();
    }
    private void SaveRules(string name)
    {
        var profile = _settings.Profiles.FirstOrDefault(x => x.Name == name);
        if (profile == null) return;
        foreach (var s in _items)
            if (!s.Protected) { if (s.Desired == DesiredState.Ignore) profile.Services.Remove(s.Name); else profile.Services[s.Name] = s.Desired; }
        _settings.Save();
    }
    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Current is null) return;
        SaveRules(_previousProfile);
        _loading = true;
        foreach (var s in _items)
            s.Desired = !s.Protected && Current.Services.TryGetValue(s.Name, out var choice) ? choice : DesiredState.Ignore;
        _settings.SelectedProfile = Current.Name;
        _previousProfile = Current.Name;
        _settings.Save();
        ProfileDescription.Text = Current.Description;
        ShutdownWslBox.IsChecked = Current.ShutdownWsl;
        LaunchDockerBox.IsChecked = Current.LaunchDockerDesktop;
        RulesValue.Text = _items.Count(s => s.Desired != DesiredState.Ignore).ToString();
        _loading = false;
    }
    private void Clone_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } original) return;
        SaveRules(original.Name);
        int i = 2;
        string name;
        do { name = original.Name + " " + i++; }
        while (_settings.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var clone = new Profile { Name = name, Description = original.Description, ShutdownWsl = original.ShutdownWsl,
            LaunchDockerDesktop = original.LaunchDockerDesktop, Services = new(original.Services, StringComparer.OrdinalIgnoreCase),
            CloseProcesses = [.. original.CloseProcesses] };
        _settings.Profiles.Add(clone);
        ProfilePicker.Items.Refresh();
        ProfilePicker.SelectedItem = clone;
        _settings.Save();
        Log("Perfil duplicado: " + name);
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } selected || _settings.Profiles.Count < 2) return;
        if (MessageBox.Show($"¿Eliminar el perfil «{selected.Name}»?", "Eliminar perfil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _loading = true;
        _settings.Profiles.Remove(selected);
        ProfilePicker.Items.Refresh();
        ProfilePicker.SelectedIndex = 0;
        _previousProfile = Current?.Name ?? "";
        _settings.SelectedProfile = _previousProfile;
        _loading = false;
        _settings.Save();
        _ = RefreshAsync();
    }
    private void ProfileOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || Current is not { } profile) return;
        profile.ShutdownWsl = ShutdownWslBox.IsChecked == true;
        profile.LaunchDockerDesktop = LaunchDockerBox.IsChecked == true;
        _settings.Save();
    }
    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ApplyAtLogin = ApplyAtLogin.IsChecked == true;
        _settings.Save();
    }
    private string DescribePlan()
    {
        var profile = Current;
        if (profile is null) return "Sin perfil.";
        var lines = new List<string> { $"PERFIL: {profile.Name}", "" };
        foreach (var s in _items.Where(s => !s.Protected && s.Desired != DesiredState.Ignore))
        {
            string action = s.Desired == DesiredState.Stop ? "Detener" : "Iniciar/conservar";
            bool currentlyRunning = s.State.Equals("Running", StringComparison.OrdinalIgnoreCase);
            string change = (s.Desired == DesiredState.Stop && currentlyRunning) ||
                            (s.Desired == DesiredState.KeepRunning && !currentlyRunning) ? "CAMBIO" : "Sin cambio";
            lines.Add($"{action}: {s.Name} ({change})");
        }
        if (profile.ShutdownWsl) lines.Add("WSL: apagar distribuciones");
        if (profile.LaunchDockerDesktop) lines.Add("Docker Desktop: abrir si no está activo");
        if (profile.CloseProcesses.Count > 0) lines.Add("Solicitar cierre de ventanas: " + string.Join(", ", profile.CloseProcesses));
        lines.Add("");
        lines.Add("Los servicios protegidos permanecen intactos; no se cambia su inicio automático.");
        return string.Join(Environment.NewLine, lines);
    }
    private void Preview_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(DescribePlan(), "Vista previa · System Warmup", MessageBoxButton.OK, MessageBoxImage.Information);

    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync(fromStartup: false);
    private async Task ApplyAsync(bool fromStartup)
    {
        if (_busy || Current is not { } profile) return;
        string plan = DescribePlan();
        if (!fromStartup && MessageBox.Show(plan + "\n\n¿Aplicar ahora?", "Confirmar cambios",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _busy = true;
        ApplyButton.IsEnabled = false;
        try
        {
            Log($"Aplicando «{profile.Name}» {(fromStartup ? "al iniciar sesión" : "manualmente")}.");
            // Do not terminate protected services or force-kill processes.
            foreach (var item in _items.Where(s => !s.Protected && s.Desired != DesiredState.Ignore).ToArray())
                Log(await SystemOperations.ApplyServiceAsync(item, item.Desired));
            foreach (string message in SystemOperations.CloseOptionalProcesses(profile.CloseProcesses))
                Log(message);
            if (profile.ShutdownWsl) Log(await SystemOperations.ShutdownWslAsync());
            if (profile.LaunchDockerDesktop) Log(SystemOperations.LaunchDocker());
        }
        catch (Exception ex) { Log("ERROR: " + ex); MessageBox.Show(ex.Message, "Aplicación incompleta"); }
        finally { _busy = false; ApplyButton.IsEnabled = true; }
        await RefreshAsync();
    }
    private async void InstallStartup_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Se creará una tarea al iniciar sesión con privilegios elevados. ¿Continuar?",
            "Inicio con Windows", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { Log(await SystemOperations.SetAutorunAsync(true)); }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
    }
    private async void RemoveStartup_Click(object sender, RoutedEventArgs e)
    {
        try { Log(await SystemOperations.SetAutorunAsync(false)); }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
    }
}