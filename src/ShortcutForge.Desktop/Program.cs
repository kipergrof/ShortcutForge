using Avalonia;

namespace ShortcutForge.Desktop;

internal static class Program
{
    // Avalonia is not ready before AppMain is called: do not use any Avalonia API before that.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
