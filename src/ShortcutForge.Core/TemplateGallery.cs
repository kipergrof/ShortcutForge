using System.Text.Json;
using System.Text.Json.Serialization;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core;

/// <summary>A template listed in the online gallery (templates/index.json in the repository).</summary>
public sealed class GalleryTemplate
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public Dictionary<string, string> Name { get; init; } = [];
    public Dictionary<string, string> Description { get; init; } = [];
    public Dictionary<string, string> Files { get; init; } = [];

    /// <summary>Value for the UI language, falling back to English, then to anything.</summary>
    private static string Pick(Dictionary<string, string> values) =>
        values.TryGetValue(L.Language, out var v) ? v
        : values.TryGetValue(L.English, out var en) ? en
        : values.Values.FirstOrDefault() ?? "";

    [JsonIgnore] public string DisplayName => Pick(Name);
    [JsonIgnore] public string DisplayDescription => Pick(Description);
    [JsonIgnore] public string FileName => Pick(Files);

    public override string ToString() => DisplayName;
}

/// <summary>Downloads templates from the gallery folder on GitHub, so new ones need no app release.</summary>
public static class TemplateGallery
{
    public const string DefaultBaseUrl = "https://raw.githubusercontent.com/kipergrof/ShortcutForge/main/templates/";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class Index
    {
        public int Version { get; init; }
        public List<GalleryTemplate> Templates { get; init; } = [];
    }

    public static IReadOnlyList<GalleryTemplate> ParseIndex(string json) =>
        (JsonSerializer.Deserialize<Index>(json, JsonOptions)?.Templates ?? [])
            .Where(t => t.Id.Length > 0 && t.Files.Count > 0)
            .ToList();

    public static async Task<IReadOnlyList<GalleryTemplate>> GetIndexAsync(HttpClient? httpClient = null,
        string baseUrl = DefaultBaseUrl, CancellationToken cancellationToken = default) =>
        ParseIndex(await GetStringAsync(baseUrl + "index.json", httpClient, cancellationToken));

    /// <summary>The template's text-language source in the UI language.</summary>
    public static Task<string> GetSourceAsync(GalleryTemplate template, HttpClient? httpClient = null,
        string baseUrl = DefaultBaseUrl, CancellationToken cancellationToken = default)
    {
        var file = template.FileName;
        if (file.Contains("..") || file.Contains('/') || file.Contains('\\'))
            throw new InvalidOperationException(L.T("Érvénytelen sablonfájl-név.", "Invalid template file name."));
        return GetStringAsync(baseUrl + Uri.EscapeDataString(file), httpClient, cancellationToken);
    }

    private static async Task<string> GetStringAsync(string url, HttpClient? httpClient, CancellationToken cancellationToken)
    {
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(AppInfo.UserAgent);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        finally
        {
            if (httpClient is null) client.Dispose();
        }
    }
}
