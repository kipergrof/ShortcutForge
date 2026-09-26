using System.Text.Json;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Desktop.Services;

/// <summary>The few settings the desktop preview remembers, stored as JSON in the user's config folder.</summary>
public sealed class DesktopSettings
{
    public string Language { get; set; } = L.English;

    /// <summary>The user agreed that shortcuts may be sent to Shortcuty for signing.</summary>
    public bool ShortcutyConsent { get; set; }

    public List<string> RecentFiles { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create),
        "ShortcutForge", "desktop-settings.json");

    /// <summary>Where the settings are stored; null keeps them in memory only (tests).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; set; }

    public static DesktopSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(path), JsonOptions);
                if (loaded is not null)
                {
                    loaded.FilePath = path;
                    if (loaded.Language != L.Hungarian) loaded.Language = L.English;
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // unreadable settings: start with the defaults
        }
        return new DesktopSettings { FilePath = path };
    }

    public void Save()
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // settings are a convenience; ignore a read-only config folder
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }
}
