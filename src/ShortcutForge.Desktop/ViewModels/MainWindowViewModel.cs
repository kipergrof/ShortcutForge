using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Desktop.Services;
using ShortcutForge.Dsl;
using ShortcutForge.Editor;
using ShortcutForge.Signing;

namespace ShortcutForge.Desktop.ViewModels;

/// <summary>Dialogs the view model needs from the window (asynchronous, as in Avalonia).</summary>
public interface IDesktopDialogs
{
    /// <summary>Asks for a shortcut file to open; returns its local path or null.</summary>
    Task<string?> OpenFileAsync();

    /// <summary>Asks where to save; returns the local path or null.</summary>
    Task<string?> SaveFileAsync(string title, string suggestedName, string extension, string typeName);

    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Yes = true, No = false, Cancel = null.</summary>
    Task<bool?> AskYesNoCancelAsync(string title, string message);

    Task InfoAsync(string title, string message);

    Task ErrorAsync(string title, string message);
}

/// <summary>The main window: the shared editor plus files, export / signing and language.</summary>
public sealed partial class MainWindowViewModel : EditorViewModel
{
    private readonly IDesktopDialogs _dialogs;
    private readonly DesktopSettings _settings;

    public MainWindowViewModel(IDesktopDialogs dialogs, DesktopSettings settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        RecentFiles = new ObservableCollection<string>(settings.RecentFiles);
    }

    /// <summary>The native <c>shortcuts sign</c> tool is offered only on macOS.</summary>
    public bool CanSignOnThisMac => MacLocalSigner.IsAvailable;

    public bool IsEnglish => L.IsEnglish;
    public bool IsHungarian => !L.IsEnglish;

    public ObservableCollection<string> RecentFiles { get; }

    public bool HasRecentFiles => RecentFiles.Count > 0;

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsHungarian));
    }

    [RelayCommand]
    private void SetLanguage(string? language)
    {
        _settings.Language = language == L.Hungarian ? L.Hungarian : L.English;
        _settings.Save();
        L.Language = _settings.Language;
    }

    private string TextErrorMessage => L.T("A szöveges nézetben hiba van:\n", "The text view has an error:\n") + DslError;

    // ------------------------------------------------------------------ new / open / save

    /// <summary>Applies or (after asking) drops invalid text edits, then offers to save unsaved changes.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!ApplyDslIfNeeded())
        {
            if (!await _dialogs.ConfirmAsync(L.T("Hibás szöveg", "Invalid text"),
                    L.T("A szöveges módosítások hibásak és elvesznek. Folytatod?", "The text changes have an error and will be lost. Continue?")))
                return false;
            DiscardDslEdits();
        }
        if (!IsDirty) return true;
        var answer = await _dialogs.AskYesNoCancelAsync(L.T("Nem mentett változások", "Unsaved changes"),
            L.T("Mented a módosításokat?", "Save your changes?"));
        if (answer is null) return false;
        return answer == false || await SaveAsync();
    }

    /// <summary>Called when the window is closing.</summary>
    public Task<bool> CanCloseAsync() => ConfirmDiscardAsync();

    [RelayCommand]
    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        LoadShortcut(new Shortcut(), null);
        Status = L.T("Új parancs.", "New shortcut.");
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await _dialogs.OpenFileAsync();
        if (path is not null) await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (path is null || !await ConfirmDiscardAsync()) return;
        await OpenPathAsync(path);
    }

    /// <summary>Opens a file without asking about unsaved changes (start-up argument, after confirming).</summary>
    public async Task OpenPathAsync(string path)
    {
        try
        {
            var shortcut = ShortcutDocument.Open(path);
            var isProject = ShortcutDocument.IsProject(path);
            LoadShortcut(shortcut, isProject ? path : null);
            AddRecent(path);
            Status = L.T($"Megnyitva: {path}", $"Opened: {path}");
            if (!isProject)
                Status += L.T(" (importálva; mentéskor .sfdsl fájl készül, exporttal .shortcut)", " (imported; Save creates a .sfdsl file, Export a .shortcut)");
        }
        catch (Exception ex) when (ex is IOException or FormatException or DslException or InvalidDataException
                                       or UnauthorizedAccessException or ArgumentException)
        {
            await _dialogs.ErrorAsync(L.T("Megnyitás sikertelen", "Could not open the file"), ex.Message);
        }
    }

    private void AddRecent(string path)
    {
        _settings.AddRecent(path);
        _settings.Save();
        RecentFiles.Clear();
        foreach (var p in _settings.RecentFiles) RecentFiles.Add(p);
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    [RelayCommand]
    private Task SaveFileAsync() => SaveAsync();

    [RelayCommand]
    private Task SaveFileAsAsync() => SaveAsAsync();

    public Task<bool> SaveAsync() => FilePath is null ? SaveAsAsync() : SaveToAsync(FilePath);

    public async Task<bool> SaveAsAsync()
    {
        var path = await _dialogs.SaveFileAsync(L.T("Mentés másként", "Save as"),
            ShortcutDocument.SafeFileName(Current.Name) + ShortcutDocument.ProjectExtension,
            ShortcutDocument.ProjectExtension, L.T("ShortcutForge szöveg", "ShortcutForge text"));
        return path is not null && await SaveToAsync(path);
    }

    private async Task<bool> SaveToAsync(string path)
    {
        if (!ApplyDslIfNeeded())
        {
            await _dialogs.ErrorAsync(L.T("Mentés", "Save"), TextErrorMessage);
            return false;
        }
        try
        {
            ShortcutDocument.SaveProject(Current, path);
            FilePath = path;
            IsDirty = false;
            AddRecent(path);
            Status = L.T($"Mentve: {path}", $"Saved: {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ErrorAsync(L.T("Mentés sikertelen", "Save failed"), ex.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------ export

    [RelayCommand]
    private Task ExportUnsignedAsync() => ExportAsync(new UnsignedExporter());

    [RelayCommand]
    private async Task ExportShortcutyAsync()
    {
        if (!_settings.ShortcutyConsent)
        {
            if (!await _dialogs.ConfirmAsync(L.T("Ingyenes online aláírás", "Free online signing"),
                    L.T("A parancsot a Shortcuty online szolgáltatás írja alá ingyen, így az iPhone is importálja.\n\n" +
                        "A parancs tartalma a Shortcuty szerverére kerül (jelszót, személyes adatot ne küldj így). Folytatod?",
                        "The shortcut is signed for free by the Shortcuty online service, so iPhone can import it.\n\n" +
                        "The shortcut's content is sent to Shortcuty's server (don't send passwords or personal data this way). Continue?")))
                return;
            _settings.ShortcutyConsent = true;
            _settings.Save();
        }
        await ExportAsync(new ShortcutySigner());
    }

    [RelayCommand]
    private async Task ExportSignedOnMacAsync()
    {
        if (!CanSignOnThisMac)
        {
            await _dialogs.ErrorAsync(L.T("Aláírás", "Signing"),
                L.T("A helyi aláíráshoz macOS 12 vagy újabb kell ('shortcuts' parancs).", "Local signing needs macOS 12 or later (the 'shortcuts' command)."));
            return;
        }
        await ExportAsync(new MacLocalSigner());
    }

    /// <summary>Checks the shortcut before export: the text view must be valid, and blocks closed (or confirmed).</summary>
    private async Task<bool> ReadyToExportAsync()
    {
        if (!ApplyDslIfNeeded())
        {
            await _dialogs.ErrorAsync(L.T("Exportálás", "Export"), TextErrorMessage);
            return false;
        }
        return ControlFlow.Validate(Current.Actions) is not { } problem ||
               await _dialogs.ConfirmAsync(L.T("Szerkezeti hiba", "Structure error"), problem + L.T("\n\nÍgy is exportálod?", "\n\nExport anyway?"));
    }

    private async Task ExportAsync(ISigner signer)
    {
        if (!await ReadyToExportAsync()) return;
        var path = await _dialogs.SaveFileAsync(L.T("Exportálás", "Export"),
            ShortcutDocument.SafeFileName(Current.Name) + ".shortcut", ".shortcut", L.T("Shortcut fájl", "Shortcut file"));
        if (path is null) return;

        try
        {
            IsBusy = true;
            Status = L.T($"Exportálás: {signer.DisplayName}…", $"Exporting: {signer.DisplayName}…");
            var bytes = await signer.SignAsync(ShortcutDocument.UnsignedBytes(Current), SigningMode.Anyone, Current.Name);
            await File.WriteAllBytesAsync(path, bytes);

            if (signer.ProducesSignedFile)
            {
                Status = L.T($"Aláírt shortcut exportálva: {path}", $"Signed shortcut exported: {path}");
                await _dialogs.InfoAsync(L.T("Kész", "Done"),
                    L.T("Az aláírt shortcut elkészült. AirDroppal, e-mailben vagy iCloud Drive-on át megnyitható iPhone-on, iPaden és Macen.",
                        "The signed shortcut is ready. Open it on iPhone, iPad or Mac via AirDrop, email or iCloud Drive."));
            }
            else
            {
                Status = L.T($"Aláíratlan shortcut exportálva: {path}", $"Unsigned shortcut exported: {path}");
                await _dialogs.InfoAsync(L.T("Aláírás szükséges", "Signing required"),
                    L.T("A fájl aláírás nélkül készült. iOS 15 óta az iPhone csak aláírt parancsot importál.\n\n", "The file is unsigned. Since iOS 15, iPhone only imports signed shortcuts.\n\n") +
                    L.T("Aláírás Macen (Terminál):\n", "To sign on a Mac (Terminal):\n") +
                    $"  shortcuts sign --mode anyone --input \"{Path.GetFileName(path)}\" --output \"signed.shortcut\"\n\n" +
                    L.T("Mac nélkül, ingyen: Fájl › Exportálás aláírva (Shortcuty, online).", "Without a Mac, for free: File › Export signed (Shortcuty, online)."));
            }
        }
        catch (SigningException ex)
        {
            Status = L.T("Aláírás sikertelen.", "Signing failed.");
            await _dialogs.ErrorAsync(L.T("Aláírás sikertelen", "Signing failed"), ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ErrorAsync(L.T("Exportálás sikertelen", "Export failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportXmlPlistAsync()
    {
        if (!await ReadyToExportAsync()) return;
        var path = await _dialogs.SaveFileAsync(L.T("Exportálás XML plistként", "Export as XML plist"),
            ShortcutDocument.SafeFileName(Current.Name) + ".plist", ".plist", "XML plist");
        if (path is null) return;
        try
        {
            await File.WriteAllTextAsync(path, PlistSerializer.WriteXml(Current));
            Status = L.T($"XML plist exportálva: {path}", $"XML plist exported: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ErrorAsync(L.T("Exportálás sikertelen", "Export failed"), ex.Message);
        }
    }
}
