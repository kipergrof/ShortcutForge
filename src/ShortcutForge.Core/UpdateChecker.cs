using System.Net.Http.Headers;
using System.Text.Json;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core;

/// <summary>A newer release available on GitHub.</summary>
public sealed record UpdateInfo(string Version, string PageUrl, string? DownloadUrl, string? Notes);

/// <summary>Result of an update check: <see cref="Update"/> is null when up to date; <see cref="Error"/> on failure.</summary>
public sealed record UpdateCheckResult(UpdateInfo? Update, string? Error = null)
{
    public static readonly UpdateCheckResult UpToDate = new(Update: null);
}

/// <summary>Checks the latest GitHub release of ShortcutForge.</summary>
public static class UpdateChecker
{
    public const string LatestReleaseApi = "https://api.github.com/repos/kipergrof/ShortcutForge/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(string currentVersion, HttpClient? httpClient = null,
        CancellationToken cancellationToken = default)
    {
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return UpdateCheckResult.UpToDate; // no release yet
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(null, L.T($"A GitHub hibát adott (HTTP {(int)response.StatusCode}).",
                    $"GitHub returned an error (HTTP {(int)response.StatusCode})."));

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
                root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                return UpdateCheckResult.UpToDate;

            var latest = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
            if (!IsNewer(latest, currentVersion)) return UpdateCheckResult.UpToDate;

            string? download = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase))
                        download = asset.GetProperty("browser_download_url").GetString();
                }

            return new UpdateCheckResult(new UpdateInfo(
                latest,
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? AppInfo.RepositoryUrl : AppInfo.RepositoryUrl,
                download,
                root.TryGetProperty("body", out var body) ? body.GetString() : null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            return new UpdateCheckResult(null, L.T($"Nem sikerült ellenőrizni a frissítéseket: {ex.Message}",
                $"Could not check for updates: {ex.Message}"));
        }
        finally
        {
            if (httpClient is null) client.Dispose();
        }
    }

    /// <summary>True if <paramref name="latest"/> is a higher version than <paramref name="current"/> (e.g. "1.2.0" &gt; "1.1.9").</summary>
    public static bool IsNewer(string latest, string current) =>
        Version.TryParse(Normalize(latest), out var l) && Version.TryParse(Normalize(current), out var c) && l > c;

    private static string Normalize(string version)
    {
        var core = version.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        var parts = core.Split('.');
        return parts.Length switch
        {
            1 => core + ".0.0",
            2 => core + ".0",
            _ => core,
        };
    }
}
