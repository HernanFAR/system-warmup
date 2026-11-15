using System;
using System.Threading;
using System.Windows;

namespace SystemWarmup;
public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, @"Local\SystemWarmup.SingleInstance.v1", out bool created);
        _ownsMutex = created;
        if (!created)
        {
            MessageBox.Show("System Warmup ya está ejecutándose.", "System Warmup", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}