using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ShortcutForge.App.Editor;
using ShortcutForge.Core;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Views;

/// <summary>Browses the online template gallery and returns the chosen template's source.</summary>
public partial class GalleryWindow : Window
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Dictionary<string, string> _sources = [];
    private IReadOnlyList<GalleryTemplate> _all = [];

    public GalleryWindow()
    {
        InitializeComponent();
        DslEditorSupport.Attach(Preview, () => []);
        Loaded += async (_, _) => await LoadIndexAsync();
        Closed += (_, _) => _http.Dispose();
    }

    /// <summary>Source of the template the user opened; null if cancelled.</summary>
    public string? SelectedSource { get; private set; }

    private async Task LoadIndexAsync()
    {
        StatusText.Text = L.T("Galéria betöltése…", "Loading gallery…");
        try
        {
            _all = await TemplateGallery.GetIndexAsync(_http);
            Filter();
            StatusText.Text = L.T($"{_all.Count} sablon · a galéria a GitHubról frissül, új verzió nélkül is bővül.",
                $"{_all.Count} templates · the gallery comes from GitHub and grows without app updates.");
            if (TemplateList.Items.Count > 0) TemplateList.SelectedIndex = 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            StatusText.Text = L.T($"A galéria nem érhető el: {ex.Message}", $"The gallery is not available: {ex.Message}");
        }
    }

    private void Filter()
    {
        var q = SearchBox.Text.Trim();
        TemplateList.ItemsSource = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(t => Fold(t.DisplayName + " " + t.DisplayDescription + " " + t.Category).Contains(Fold(q))).ToList();
    }

    /// <summary>Lower case without accents, for searching ("Fotók" → "fotok").</summary>
    private static string Fold(string text) =>
        new string(text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant).ToArray());

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Filter();

    private async void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenButton.IsEnabled = false;
        if (TemplateList.SelectedItem is not GalleryTemplate template) return;
        try
        {
            if (!_sources.TryGetValue(template.Id, out var source))
            {
                Preview.Text = L.T("Betöltés…", "Loading…");
                source = await TemplateGallery.GetSourceAsync(template, _http);
                _sources[template.Id] = source;
            }
            if (TemplateList.SelectedItem != template) return; // selection changed meanwhile
            Preview.Text = source;
            OpenButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Preview.Text = L.T($"// Nem sikerült letölteni: {ex.Message}", $"// Could not download: {ex.Message}");
        }
    }

    private void TemplateList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OpenButton.IsEnabled) Open_Click(sender, e);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not GalleryTemplate template || !_sources.TryGetValue(template.Id, out var source)) return;
        SelectedSource = source;
        DialogResult = true;
    }
}
