using System;
using System.Windows;
using System.Windows.Media;
using SystemWarmup;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Reproduce the original failure: WPF resources are immutable/frozen.
        var app = new Application();
        var original = new SolidColorBrush(Colors.Red);
        original.Freeze();
        app.Resources["WindowBrush"] = original;
        app.Resources["PanelBrush"] = new SolidColorBrush(Colors.Blue);
        app.Resources["TextBrush"] = new SolidColorBrush(Colors.Black);

        var manager = new ThemeManager();
        manager.Apply(dark: true);
        var dark = (SolidColorBrush)app.Resources["WindowBrush"];
        if (dark.Color != (Color)ColorConverter.ConvertFromString("#0B1020") || ReferenceEquals(dark, original))
            throw new Exception("Dark theme failed to replace the frozen resource.");

        manager.Apply(dark: false);
        var light = (SolidColorBrush)app.Resources["WindowBrush"];
        if (light.Color != (Color)ColorConverter.ConvertFromString("#F4F7FB") || ReferenceEquals(light, dark))
            throw new Exception("Light theme failed to replace the resource.");

        manager.Apply(dark: true);
        if (((SolidColorBrush)app.Resources["TextBrush"]).Color !=
            (Color)ColorConverter.ConvertFromString("#EDF4FF"))
            throw new Exception("Theme switching did not update text brushes.");
        Console.WriteLine("PASS: frozen brushes are replaced; dark-light-dark switching succeeds.");
        return 0;
    }
}