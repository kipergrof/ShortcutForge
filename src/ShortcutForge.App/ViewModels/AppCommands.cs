using System.Windows.Input;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.ViewModels;

public static class AppCommands
{
    public static readonly RoutedUICommand Help = new(L.T("Súgó", "Help"), nameof(Help), typeof(AppCommands));
}
