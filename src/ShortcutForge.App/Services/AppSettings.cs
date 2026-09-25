using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShortcutForge.Signing;

namespace ShortcutForge.App.Services;

public enum SignerKind
{
    Unsigned,
    MacSsh,
    RemoteServer,
}

/// <summary>User settings, stored in %APPDATA%\ShortcutForge\settings.json. The password is DPAPI-encrypted.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>UI language: "hu" or "en" (default: Hungarian on Hungarian Windows, otherwise English).</summary>
    public string Language { get; set; } =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hu" ? "hu" : "en";

    public SignerKind Signer { get; set; } = SignerKind.Unsigned;
    public SigningMode SigningMode { get; set; } = SigningMode.Anyone;

    /// <summary>URL of a shortcut-signing-server compatible service (SignerKind.RemoteServer).</summary>
    public string RemoteServerUrl { get; set; } = "";

    public string MacHost { get; set; } = "";
    public int MacPort { get; set; } = 22;
    public string MacUser { get; set; } = "";
    public string? MacKeyPath { get; set; }
    public string? ProtectedPassword { get; set; }

    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Settings folder; SHORTCUTFORGE_SETTINGS_DIR overrides it (used by tests).</summary>
    private static string Folder =>
        Environment.GetEnvironmentVariable("SHORTCUTFORGE_SETTINGS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShortcutForge");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Decrypted view of <see cref="ProtectedPassword"/>; never written to the JSON file.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? MacPassword
    {
        get
        {
            if (string.IsNullOrEmpty(ProtectedPassword)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return null;
            }
        }
        set => ProtectedPassword = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: start fresh.
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }

    public ISigner CreateSigner() => Signer switch
    {
        SignerKind.MacSsh => new MacSshSigner(new MacSshSettings
        {
            Host = MacHost,
            Port = MacPort,
            User = MacUser,
            Password = MacPassword,
            PrivateKeyPath = MacKeyPath,
        }),
        SignerKind.RemoteServer => new RemoteSigner(
            ShortcutForge.Core.Localization.L.T($"Aláíró szerver ({RemoteServerUrl})", $"Signing server ({RemoteServerUrl})"), RemoteServerUrl),
        _ => new UnsignedExporter(),
    };
}
