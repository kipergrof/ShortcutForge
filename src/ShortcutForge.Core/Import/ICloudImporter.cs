using System.Text.Json;
using System.Text.RegularExpressions;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Import;

/// <summary>Downloads a shared shortcut from an iCloud link (https://www.icloud.com/shortcuts/&lt;id&gt;).</summary>
public static partial class ICloudImporter
{
    [GeneratedRegex(@"icloud\.com/shortcuts/(?:api/records/)?([0-9a-fA-F]{32})")]
    private static partial Regex LinkRegex();

    public static string? ParseId(string link)
    {
        var m = LinkRegex().Match(link);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static async Task<Shortcut> ImportAsync(string link, HttpClient? http = null, CancellationToken cancellationToken = default)
    {
        var id = ParseId(link) ?? throw new FormatException(L.T("Ez nem iCloud shortcut link (https://www.icloud.com/shortcuts/…).", "This is not an iCloud shortcut link (https://www.icloud.com/shortcuts/…)."));
        var client = http ?? new HttpClient();
        try
        {
            using var response = await client.GetAsync($"https://www.icloud.com/shortcuts/api/records/{id}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(L.T($"Az iCloud nem adta vissza a shortcutot (HTTP {(int)response.StatusCode}).", $"iCloud did not return the shortcut (HTTP {(int)response.StatusCode})."));

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var fields = json.RootElement.GetProperty("fields");
            var name = fields.TryGetProperty("name", out var n) ? n.GetProperty("value").GetString() : null;

            // "shortcut" is the unsigned plist; "signedShortcut" the AEA file. Both can be read.
            string? url = null;
            foreach (var field in new[] { "shortcut", "signedShortcut" })
            {
                if (fields.TryGetProperty(field, out var f) && f.TryGetProperty("value", out var v) &&
                    v.TryGetProperty("downloadURL", out var d))
                {
                    url = d.GetString();
                    break;
                }
            }
            if (url is null) throw new InvalidOperationException(L.T("A válaszban nincs letölthető shortcut.", "The response contains no downloadable shortcut."));

            var bytes = await client.GetByteArrayAsync(url.Replace("${f}", Uri.EscapeDataString(name ?? "shortcut") + ".shortcut"), cancellationToken);
            return ShortcutFileReader.Read(bytes, name);
        }
        finally
        {
            if (http is null) client.Dispose();
        }
    }
}
