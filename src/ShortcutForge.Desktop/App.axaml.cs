using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShortcutForge.Core.Localization;
using ShortcutForge.Desktop.Services;
using ShortcutForge.Desktop.Views;

namespace ShortcutForge.Desktop;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // English is the default; the choice is remembered between runs.
        var settings = DesktopSettings.Load();
        L.Language = settings.Language;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(settings, desktop.Args ?? []);

        base.OnFrameworkInitializationCompleted();
    }
}
