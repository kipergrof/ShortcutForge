using System.Windows.Input;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.ViewModels;

public static class AppCommands
{
    public static readonly RoutedUICommand FocusSearch = new("Search", nameof(FocusSearch), typeof(AppCommands));

    public static readonly RoutedUICommand Palette = new("Command palette", nameof(Palette), typeof(AppCommands));

    public static readonly RoutedUICommand Help = new(L.T("Súgó", "Help"), nameof(Help), typeof(AppCommands));
}
