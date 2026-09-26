using System.Windows;
using ShortcutForge.App.Editor;
using ShortcutForge.App.Services;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Views;

/// <summary>Generates a shortcut from a plain-language description with Claude.</summary>
public partial class AiGenerateWindow : Window
{
    private readonly string? _apiKey;
    private CancellationTokenSource? _cts;

    public AiGenerateWindow(string? apiKey)
    {
        InitializeComponent();
        _apiKey = apiKey;
        DslEditorSupport.Attach(Preview, () => []);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText.Text = L.T("Nincs Claude API-kulcs: add meg az Eszközök › Beállítások menüben (vagy az ANTHROPIC_API_KEY környezeti változóban).",
                "No Claude API key: enter it in Tools › Settings (or set the ANTHROPIC_API_KEY environment variable).");
            GenerateButton.IsEnabled = false;
        }
        Loaded += (_, _) => DescriptionBox.Focus();
        Closed += (_, _) => _cts?.Cancel();
    }

    /// <summary>Generated code the user chose to open; null if closed.</summary>
    public string? ResultCode { get; private set; }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        var description = DescriptionBox.Text.Trim();
        if (description.Length == 0)
        {
            StatusText.Text = L.T("Írd le, mit csináljon a parancs.", "Describe what the shortcut should do.");
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        OpenButton.IsEnabled = false;
        ExplanationText.Visibility = Visibility.Collapsed;
        StatusText.Text = L.T("A Claude dolgozik a parancson… (ez fél percig is eltarthat)", "Claude is writing the shortcut… (this can take half a minute)");
        try
        {
            var result = await new ShortcutAiService(_apiKey!).GenerateAsync(description, _cts.Token);
            Preview.Text = result.Code;
            if (result.Explanation is { } explanation)
            {
                ExplanationText.Text = explanation;
                ExplanationText.Visibility = Visibility.Visible;
            }
            if (result.Valid)
            {
                StatusText.Text = L.T("Kész. Nézd át, aztán nyisd meg a szerkesztőben.", "Done. Review it, then open it in the editor.");
                OpenButton.IsEnabled = true;
            }
            else
            {
                StatusText.Text = L.T($"A kapott kódban hiba maradt, ezért nem nyitható meg: {result.Error}. Próbáld pontosabb leírással.",
                    $"The code still has an error, so it cannot be opened: {result.Error}. Try a more precise description.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = L.T("Megszakítva.", "Stopped.");
        }
        catch (AiGenerationException ex)
        {
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        GenerateButton.IsEnabled = !busy;
        DescriptionBox.IsReadOnly = busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        ResultCode = Preview.Text;
        DialogResult = true;
    }
}
