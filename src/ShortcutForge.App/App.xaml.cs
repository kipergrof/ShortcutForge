using System.Windows;
using ShortcutForge.App.Services;

namespace ShortcutForge.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = AppSettings.Load();
        ShortcutForge.Core.Localization.L.Language = settings.Language;
        ThemeManager.Apply(settings.Theme);
        base.OnStartup(e);
    }
}
