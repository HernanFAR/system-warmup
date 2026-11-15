using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace SystemWarmup;

/// <summary>Replaces WPF brush resources so DynamicResource bindings refresh without mutating frozen brushes.</summary>
public sealed class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#0B1020",
        ["PanelBrush"] = "#151E30",
        ["Panel2Brush"] = "#1D2940",
        ["BorderBrush"] = "#2C3951",
        ["AccentBrush"] = "#56E4BC",
        ["TextBrush"] = "#EDF4FF",
        ["MutedBrush"] = "#A6B5CB",
        ["GridHeaderBrush"] = "#263650",
        ["GridHeaderTextBrush"] = "#D4E1F3",
        ["LogBrush"] = "#0C1526",
        ["BadgeBrush"] = "#1B3B37",
        ["BadgeTextBrush"] = "#78E8C2",
        ["AccentTextBrush"] = "#081B17",
        ["InputBrush"] = "#EAF3FF",
        ["InputTextBrush"] = "#172239",
        ["FooterBrush"] = "#70849F"
    };

    private static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F4F7FB",
        ["PanelBrush"] = "#FFFFFF",
        ["Panel2Brush"] = "#EAF0F8",
        ["BorderBrush"] = "#D3DCE9",
        ["AccentBrush"] = "#087E68",
        ["TextBrush"] = "#14243A",
        ["MutedBrush"] = "#526782",
        ["GridHeaderBrush"] = "#E6EDF6",
        ["GridHeaderTextBrush"] = "#233A58",
        ["LogBrush"] = "#EFF4FB",
        ["BadgeBrush"] = "#DDF4EB",
        ["BadgeTextBrush"] = "#116A55",
        ["AccentTextBrush"] = "#FFFFFF",
        ["InputBrush"] = "#FFFFFF",
        ["InputTextBrush"] = "#172239",
        ["FooterBrush"] = "#63768F"
    };

    public void Apply(bool dark)
    {
        var resources = Application.Current.Resources;
        foreach (var (name, hex) in dark ? Dark : Light)
        {
            // XAML brushes may be frozen (immutable) after loading BAML.
            // Replace dictionary values instead of mutating the existing brush;
            // DynamicResource references then resolve the new value.
            resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }
}
