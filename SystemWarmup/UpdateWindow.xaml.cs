using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SystemWarmup;

public partial class UpdateWindow : Window
{
    private readonly Settings _settings;
    private readonly Version _current;
    private AvailableUpdate? _available;
    private bool _busy;

    public UpdateWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);
        CurrentVersion.Text = "Actual: " + (UpdateService.ReadInstalled()?.Identity ?? ("v" + _current.ToString(3)));
        RepositoryBox.Text = settings.Updates.Repository;
        ChannelBox.SelectedIndex = settings.Updates.Channel switch { "Preview" or "Prerelease" => 1, "Build" => 2, _ => 0 };
        PullRequestBox.Text = settings.Updates.PullRequest.ToString();
        CheckOnLaunchBox.IsChecked = settings.Updates.CheckOnLaunch;
    }

    private void Channel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (BuildSettings is not null)
            BuildSettings.Visibility = ChannelBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UpdateOptions FormOptions()
    {
        if (ChannelBox.SelectedIndex == 2 &&
            (!int.TryParse(PullRequestBox.Text, out int pr) || pr <= 0))
            throw new InvalidOperationException("Introduce un número válido de Pull Request.");
        return new()
    {
        Repository = RepositoryBox.Text.Trim(),
        Channel = (ChannelBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Stable",
        CheckOnLaunch = CheckOnLaunchBox.IsChecked == true,
        PullRequest = int.TryParse(PullRequestBox.Text, out int n) && n > 0 ? n : 1
    };
    }

    private void SaveSettings()
    {
        _settings.Updates = FormOptions();
        _settings.Save();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SaveButton.IsEnabled = !busy;
        CheckButton.IsEnabled = !busy;
        RepositoryBox.IsEnabled = !busy;
        ChannelBox.IsEnabled = !busy;
        CheckOnLaunchBox.IsEnabled = !busy;
        PullRequestBox.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && _available is not null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { SaveSettings(); }
        catch (Exception ex) { StatusText.Text = ex.Message; return; }
        _available = null;
        InstallButton.IsEnabled = false;
        StatusText.Text = "Configuración guardada. Comprueba nuevamente para utilizar el origen seleccionado.";
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            SaveSettings();
            _available = null;
            ReleaseTitle.Text = "";
            ReleaseNotes.Visibility = Visibility.Collapsed;
            StatusText.Text = "Consultando el canal seleccionado en GitHub...";
            using var service = new UpdateService();
            _available = await service.CheckAsync(_settings.Updates, _current, UpdateService.ReadInstalled());
            if (_available is null)
            {
                StatusText.Text = "No hay versiones más recientes disponibles en el canal seleccionado.";
            }
            else
            {
                ReleaseTitle.Text = _available.Tag;
                ReleaseNotes.Text = _available.Description;
                ReleaseNotes.Visibility = Visibility.Visible;
                StatusText.Text = "Versión nueva encontrada. El paquete se verificará con SHA-256 antes de reemplazar archivos.";
            }
        }
        catch (Exception ex)
        {
            _available = null;
            StatusText.Text = "No se pudo completar la consulta: " + ex.Message;
        }
        finally { SetBusy(false); }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _available is not { } release) return;
        string source = $"{release.Repository} · {release.Tag}";
        if (MessageBox.Show(
            $"Se descargará e instalará una versión publicada por {source}.\n\n" +
            "La aplicación se cerrará, conservará una copia de la instalación anterior y " +
            "volverá a abrirse. La configuración JSON local no se modificará.\n\n" +
            "Instala solo versiones de repositorios que conozcas y en los que confíes. ¿Continuar?",
            "Confirmar actualización", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        try
        {
            string executable = Environment.ProcessPath ?? "";
            string appDirectory = Path.GetDirectoryName(executable) ?? "";
            if (!Path.GetFileName(executable).Equals("SystemWarmup.exe", StringComparison.OrdinalIgnoreCase) ||
                appDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                !new[] { "SystemWarmup", "SystemWarmup-win-x64" }.Contains(Path.GetFileName(appDirectory), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Instala la aplicación en una carpeta exclusiva llamada SystemWarmup (o SystemWarmup-win-x64) antes de actualizar. No se reemplazará una carpeta compartida.");

            using var service = new UpdateService();
            var progress = new Progress<string>(text => StatusText.Text = text);
            PreparedUpdate staged = await service.PrepareAsync(release, progress);
            StatusText.Text = "Paquete validado. Iniciando actualizador externo...";

            string log = Path.Combine(staged.Directory, "apply.log");
            string metadata = UpdateService.PrepareInstallMetadata(release, staged.Directory);
            string targetMetadata = Path.Combine(Settings.Folder, "installed-update.json");
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = staged.Directory
            };
            foreach (string argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", staged.Script, "-ApplicationDirectory", appDirectory,
                "-StagedDirectory", staged.ApplicationRoot, "-OldPid", Environment.ProcessId.ToString(),
                "-LogFile", log, "-MetadataFile", metadata, "-InstalledMetadataPath", targetMetadata
            }) psi.ArgumentList.Add(argument);

            var process = Process.Start(psi);
            if (process is null) throw new InvalidOperationException("Windows no pudo iniciar el actualizador.");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Instalación cancelada o fallida: " + ex.Message;
            SetBusy(false);
        }
    }
}