using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ShortcutForge.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Switches between light and dark mode: the WPF Fluent theme for the standard controls, plus
/// the app's own palette brushes (referenced with DynamicResource so they update live).
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["WindowBg"] = ("#F2F2F7", "#1C1C1E"),
        ["PanelBg"] = ("#FFFFFF", "#242426"),
        ["CardBg"] = ("#FFFFFF", "#2C2C2E"),
        ["CardBorder"] = ("#D8D8DE", "#3A3A3C"),
        ["MarkerCardBg"] = ("#F7F7FA", "#252527"),
        ["UnknownBorder"] = ("#C9B6F2", "#6E5A9E"),
        ["Accent"] = ("#007AFF", "#0A84FF"),
        ["AccentHover"] = ("#0062CC", "#409CFF"),
        ["AccentSoft"] = ("#E5F0FF", "#1A2F4D"),
        ["TextFg"] = ("#1C1C1E", "#F2F2F7"),
        ["MutedText"] = ("#6E6E73", "#98989F"),
        ["SubtleText"] = ("#B0B0B8", "#6C6C70"),
        ["HandleText"] = ("#C7C7CC", "#5A5A5E"),
        ["HoverBg"] = ("#E5E5EA", "#3A3A3C"),
        ["FieldBg"] = ("#FAFAFC", "#1E1E20"),
        ["ErrorBrush"] = ("#FF3B30", "#FF453A"),
        ["WarningBg"] = ("#FFF4E5", "#3D2E14"),
        ["BadgeBg"] = ("#FFE5E5", "#4A1F1F"),
        ["BadgeFg"] = ("#C0392B", "#FF8A80"),
        ["ErrorPanelBg"] = ("#FFECEC", "#3B1E1E"),
        ["ErrorPanelFg"] = ("#B00020", "#FF8A80"),
        ["EditorBg"] = ("#FFFFFF", "#1E1E1E"),
        ["EditorFg"] = ("#1E1E1E", "#D4D4D4"),
        ["LineNumberFg"] = ("#A0A0A8", "#6E6E73"),
    };

    private static AppTheme _theme = AppTheme.System;
    private static bool _listening;

    public static bool IsDark { get; private set; }

    public static AppTheme Theme => _theme;

    /// <summary>Raised after the effective theme changed (e.g. to recolor the code editor).</summary>
    public static event EventHandler? ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        _theme = theme;
        var app = Application.Current;
        if (app is null) return;

        IsDark = theme == AppTheme.Dark || (theme == AppTheme.System && SystemPrefersDark());
        app.ThemeMode = IsDark ? ThemeMode.Dark : ThemeMode.Light;

        foreach (var (key, (light, dark)) in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark ? dark : light));
            brush.Freeze();
            app.Resources[key] = brush;
        }

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category == UserPreferenceCategory.General && _theme == AppTheme.System)
                    app.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
            };
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Windows "app mode" setting (Settings › Personalization › Colors).</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
