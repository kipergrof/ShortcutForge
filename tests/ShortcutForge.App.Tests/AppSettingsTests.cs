using System.IO;
using ShortcutForge.App.Services;

namespace ShortcutForge.App.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("SHORTCUTFORGE_SETTINGS_DIR", _dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Password_survives_save_and_load_encrypted()
    {
        var settings = new AppSettings { MacHost = "mac.local", Language = "en", Theme = AppTheme.Dark };
        settings.MacPassword = "titok123";
        settings.Save();

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain("titok123", json);
        Assert.DoesNotContain("MacPassword", json);

        var loaded = AppSettings.Load();
        Assert.Equal("titok123", loaded.MacPassword);
        Assert.Equal("mac.local", loaded.MacHost);
        Assert.Equal("en", loaded.Language);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
    }
}
