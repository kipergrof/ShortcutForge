using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShortcutForge.App.Services;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Import;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Dsl;
using ShortcutForge.Signing;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.ViewModels;

/// <summary>Dialogs and other UI services the view model needs from the window.</summary>
public interface IDialogService
{
    string? OpenFile(string filter);
    string? SaveFile(string filter, string fileName);
    string? Prompt(string title, string message, string initial = "");
    bool Confirm(string title, string message);
    bool? AskYesNoCancel(string title, string message);
    void Info(string title, string message);
    void Error(string title, string message);
    bool EditSettings(AppSettings settings);
}

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxUndo = 200;

    private readonly IDialogService _dialogs;
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private bool _suppressCheckpoint;
    private bool _checkpointTaken;
    private bool _loadingSettings;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Settings = AppSettings.Load();

        Library = CollectionViewSource.GetDefaultView(ActionCatalog.Default.Actions);
        Library.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionDefinition.DisplayCategory)));
        Library.Filter = o => o is ActionDefinition a && MatchesSearch(a);

        InputClassOptions = KnownInputClasses.Select(c => new CheckOption(c.Key, c.Label, OnInputClassesChanged)).ToList();
        TypeOptions = KnownTypes.Select(c => new CheckOption(c.Key, c.Label, OnTypesChanged)).ToList();

        L.Changed += (_, _) => OnLanguageChanged();
        ThemeManager.ThemeChanged += (_, _) => RaiseAppearanceChanged();

        LoadShortcut(DslParser.Parse(ShortcutDocument.ExampleSource), null);
    }

    // ------------------------------------------------------------------ appearance & language

    public bool IsThemeSystem => Settings.Theme == AppTheme.System;
    public bool IsThemeLight => Settings.Theme == AppTheme.Light;
    public bool IsThemeDark => Settings.Theme == AppTheme.Dark;
    public bool IsHungarian => !L.IsEnglish;
    public bool IsEnglish => L.IsEnglish;

    /// <summary>Toolbar toggle: switches between light and dark.</summary>
    public string NextTheme => ThemeManager.IsDark ? nameof(AppTheme.Light) : nameof(AppTheme.Dark);

    public string ThemeButtonText => ThemeManager.IsDark ? L.T("☀ Világos", "☀ Light") : L.T("🌙 Sötét", "🌙 Dark");

    public string OtherLanguage => L.IsEnglish ? L.Hungarian : L.English;

    private void RaiseAppearanceChanged()
    {
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(NextTheme));
        OnPropertyChanged(nameof(ThemeButtonText));
        OnPropertyChanged(nameof(OtherLanguage));
    }

    [RelayCommand]
    private void SetTheme(string? theme)
    {
        Settings.Theme = Enum.TryParse<AppTheme>(theme, out var t) ? t : AppTheme.System;
        Settings.Save();
        ThemeManager.Apply(Settings.Theme);
        RaiseAppearanceChanged();
    }

    [RelayCommand]
    private void SetLanguage(string? language)
    {
        Settings.Language = language == L.English ? L.English : L.Hungarian;
        Settings.Save();
        L.Language = Settings.Language;
    }

    /// <summary>Re-evaluates every text that comes from code rather than XAML.</summary>
    private void OnLanguageChanged()
    {
        foreach (var o in InputClassOptions) o.Label = KnownInputClasses.First(k => k.Key == o.Key).Label;
        foreach (var o in TypeOptions) o.Label = KnownTypes.First(k => k.Key == o.Key).Label;
        Library.GroupDescriptions.Clear();
        Library.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionDefinition.DisplayCategory)));
        RebuildCards(SelectedCard?.Index);
        OnPropertyChanged(nameof(IsHungarian));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(ActionCountText));
        OnPropertyChanged(nameof(Templates));
        RaiseAppearanceChanged();
        if (DslError is not null && PendingDsl is not null) OnDslEdited(PendingDsl);
        Status = L.T("Nyelv: magyar", "Language: English");
    }

    public AppSettings Settings { get; }

    public Shortcut Current { get; private set; } = new();

    public NameScope Scope { get; private set; } = new();

    public ObservableCollection<ActionCardViewModel> Cards { get; } = [];

    /// <summary>Actions whose cards are collapsed (kept across card rebuilds).</summary>
    public HashSet<ActionInstance> CollapsedActions { get; } = new(ReferenceEqualityComparer.Instance);

    [RelayCommand]
    private void CollapseAll(string? collapse)
    {
        var value = collapse != "false";
        foreach (var card in Cards.Where(c => c.CanCollapse)) card.IsCollapsed = value;
    }

    [ObservableProperty] private ActionCardViewModel? _selectedCard;

    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = L.T("Kész.", "Ready.");
    [ObservableProperty] private bool _isBusy;

    public string WindowTitle => $"{(IsDirty ? "● " : "")}{Current.Name} – ShortcutForge";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    public IReadOnlyList<string> VariableSuggestions { get; private set; } = [];

    // ------------------------------------------------------------------ library

    public ICollectionView Library { get; }

    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value) => Library.Refresh();

    private bool MatchesSearch(ActionDefinition a)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.Dsl.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.DisplayCategory.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               (a.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (a.DescriptionEn?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public int CatalogCount => ActionCatalog.Default.Actions.Count;

    // ------------------------------------------------------------------ shortcut settings

    [ObservableProperty] private string _shortcutName = "";
    [ObservableProperty] private ColorOption? _selectedColor;
    [ObservableProperty] private long _glyphNumber;

    public IReadOnlyList<ColorOption> ColorOptions { get; } =
        ShortcutIcon.Colors.Select(c => new ColorOption(c.Name, c.Value)).ToList();

    public IReadOnlyList<CheckOption> InputClassOptions { get; }
    public IReadOnlyList<CheckOption> TypeOptions { get; }

    private static (string Key, string Label)[] KnownInputClasses =>
    [
        ("WFStringContentItem", L.T("Szöveg", "Text")), ("WFURLContentItem", "URL"), ("WFImageContentItem", L.T("Kép", "Image")),
        ("WFGenericFileContentItem", L.T("Fájl", "File")), ("WFPDFContentItem", "PDF"), ("WFRichTextContentItem", L.T("Formázott szöveg", "Rich text")),
        ("WFSafariWebPageContentItem", L.T("Safari weboldal", "Safari web page")), ("WFArticleContentItem", L.T("Cikk", "Article")), ("WFContactContentItem", L.T("Kontakt", "Contact")),
        ("WFDateContentItem", L.T("Dátum", "Date")), ("WFEmailAddressContentItem", L.T("E-mail cím", "Email address")), ("WFPhoneNumberContentItem", L.T("Telefonszám", "Phone number")),
        ("WFLocationContentItem", L.T("Hely", "Location")), ("WFDCMapsLinkContentItem", L.T("Térkép link", "Maps link")), ("WFAVAssetContentItem", L.T("Média", "Media")),
        ("WFAppStoreAppContentItem", L.T("App Store app", "App Store app")), ("WFiTunesProductContentItem", L.T("iTunes termék", "iTunes product")),
    ];

    private static (string Key, string Label)[] KnownTypes =>
    [
        ("NCWidget", L.T("Widgetben", "In widgets")), ("ActionExtension", L.T("Megosztási lapon", "In the share sheet")), ("WatchKit", L.T("Apple Watch-on", "On Apple Watch")),
        ("QuickActions", L.T("Gyorsműveletként (Mac)", "As a Quick Action (Mac)")), ("MenuBar", L.T("Menüsorban (Mac)", "In the menu bar (Mac)")),
        ("ReceivesOnScreenContent", L.T("Képernyő tartalmának fogadása", "Receive on-screen content")),
    ];

    partial void OnShortcutNameChanged(string value)
    {
        if (_loadingSettings) return;
        Checkpoint();
        Current.Name = value;
        MarkDirty();
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnSelectedColorChanged(ColorOption? value)
    {
        if (_loadingSettings || value is null) return;
        Checkpoint();
        Current.Icon = Current.Icon with { StartColor = value.Value };
        MarkDirty();
    }

    partial void OnGlyphNumberChanged(long value)
    {
        if (_loadingSettings) return;
        Checkpoint();
        Current.Icon = Current.Icon with { GlyphNumber = value };
        MarkDirty();
    }

    private void OnInputClassesChanged()
    {
        if (_loadingSettings) return;
        Checkpoint();
        var known = InputClassOptions.Select(o => o.Key).ToHashSet();
        Current.InputClasses = Current.InputClasses.Where(c => !known.Contains(c))
            .Concat(InputClassOptions.Where(o => o.IsChecked).Select(o => o.Key)).ToList();
        MarkDirty();
    }

    private void OnTypesChanged()
    {
        if (_loadingSettings) return;
        Checkpoint();
        var known = TypeOptions.Select(o => o.Key).ToHashSet();
        Current.Types = Current.Types.Where(c => !known.Contains(c))
            .Concat(TypeOptions.Where(o => o.IsChecked).Select(o => o.Key)).ToList();
        MarkDirty();
    }

    private void LoadSettingsPanel()
    {
        _loadingSettings = true;
        try
        {
            ShortcutName = Current.Name;
            SelectedColor = ColorOptions.FirstOrDefault(c => c.Value == Current.Icon.StartColor)
                            ?? new ColorOption(L.T("egyéni", "custom"), Current.Icon.StartColor);
            GlyphNumber = Current.Icon.GlyphNumber;
            foreach (var o in InputClassOptions) o.SetSilently(Current.InputClasses.Contains(o.Key));
            foreach (var o in TypeOptions) o.SetSilently(Current.Types.Contains(o.Key));
        }
        finally
        {
            _loadingSettings = false;
        }
        OnPropertyChanged(nameof(WindowTitle));
    }

    // ------------------------------------------------------------------ cards

    private void LoadShortcut(Shortcut shortcut, string? path)
    {
        Current = shortcut;
        FilePath = path;
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        LoadSettingsPanel();
        RebuildCards();
        DslSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Recreates all cards from the model (after structural changes).</summary>
    /// <summary>Raised before the cards are recreated (the view saves its scroll position).</summary>
    public event EventHandler? CardsRebuilding;

    /// <summary>Raised after the cards were recreated, with the index of the card to bring into view.</summary>
    public event EventHandler<int?>? CardsRebuilt;

    public void RebuildCards(int? select = null)
    {
        CardsRebuilding?.Invoke(this, EventArgs.Empty);
        RefreshScope();
        var indent = ControlFlow.ComputeIndent(Current.Actions);
        Cards.Clear();
        for (var i = 0; i < Current.Actions.Count; i++)
            Cards.Add(new ActionCardViewModel(this, Current.Actions[i], i, indent[i]));
        foreach (var c in Cards) c.Refresh();
        if (select is { } s && s >= 0 && s < Cards.Count) SelectedCard = Cards[s];
        OnPropertyChanged(nameof(ActionCountText));
        StructureWarning = ControlFlow.Validate(Current.Actions);
        CardsRebuilt?.Invoke(this, select);
    }

    [ObservableProperty] private string? _structureWarning;

    public string VersionText => $"v{ShortcutForge.Core.AppInfo.Version}";

    public string ActionCountText => L.T($"{Current.Actions.Count} akció", $"{Current.Actions.Count} actions");

    private void RefreshScope()
    {
        Scope = DslPrinter.BuildScope(Current.Actions);
        VariableSuggestions = NameScope.Builtins.Keys
            .Concat(Scope.OutputNames)
            .Concat(Scope.NamedVariables)
            .Distinct()
            .ToList();
    }

    /// <summary>Called after a parameter value changed in the visual editor.</summary>
    public void OnParameterEdited(ParamViewModel? source)
    {
        MarkDirty();
        RefreshScope();
        // Other fields may show names that changed; the edited field keeps its text.
        foreach (var card in Cards) card.Refresh(source);
        _checkpointTaken = false;
    }

    /// <summary>Keeps the menu's WFMenuItems in sync with its case titles.</summary>
    public void SyncMenuItems(string? group)
    {
        if (group is null) return;
        var start = Current.Actions.FirstOrDefault(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.Start);
        if (start is null) return;
        var titles = Current.Actions
            .Where(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.Middle)
            .Select(a => (ParamValue)(a.Get("WFMenuItemTitle") as StringValue ?? new StringValue("")))
            .ToList();
        start.Parameters["WFMenuItems"] = new ArrayValue(titles);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        DslSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ undo

    private sealed record Snapshot(byte[] Plist, string Name);

    private Snapshot TakeSnapshot() => new(PlistSerializer.WriteBinary(Current), Current.Name);

    /// <summary>Records the state before a change (once per edit burst).</summary>
    public void Checkpoint()
    {
        if (_suppressCheckpoint || _checkpointTaken) return;
        _undo.Push(TakeSnapshot());
        if (_undo.Count > MaxUndo)
        {
            var keep = _undo.Take(MaxUndo).Reverse().ToList();
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
        _redo.Clear();
        _checkpointTaken = true;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void Restore(Snapshot snapshot)
    {
        Current = PlistSerializer.Read(snapshot.Plist, snapshot.Name);
        _checkpointTaken = false;
        LoadSettingsPanel();
        RebuildCards();
        MarkDirty();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (!ApplyDslIfNeeded()) return;
        _redo.Push(TakeSnapshot());
        Restore(_undo.Pop());
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndo() => _undo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!ApplyDslIfNeeded()) return;
        _undo.Push(TakeSnapshot());
        Restore(_redo.Pop());
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private bool CanRedo() => _redo.Count > 0;

    // ------------------------------------------------------------------ editing commands

    [RelayCommand]
    public void AddAction(ActionDefinition? definition) => InsertAction(definition, null);

    public void InsertAction(ActionDefinition? definition, int? index)
    {
        if (definition is null || !ApplyDslIfNeeded()) return;
        _checkpointTaken = false;
        Checkpoint();
        List<ActionInstance> actions;
        if (definition.Control != ControlFlowKind.None)
            actions = ControlFlow.CreateBlock(definition);
        else
        {
            var a = new ActionInstance(definition.Id);
            if (definition.Output is not null) a.EnsureUuid();
            actions = [a];
        }

        // Right after the selected card: inside a block when its "If" / "Otherwise" / menu item is selected.
        var at = index ?? (SelectedCard is { } sel ? sel.Index + 1 : Current.Actions.Count);
        at = Math.Clamp(at, 0, Current.Actions.Count);
        Current.Actions.InsertRange(at, actions);
        if (definition.Control == ControlFlowKind.Menu) SyncMenuItems(actions[0].GroupingIdentifier);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(at);
        Status = L.T($"Hozzáadva: {definition.Name}", $"Added: {definition.Name}");
    }

    /// <summary>Range of actions a card operation affects (a whole block for its start/end card).</summary>
    private (int First, int Last) OperationRange(ActionCardViewModel card)
    {
        var actions = Current.Actions;
        var i = card.Index;
        var a = actions[i];
        if (a.ControlFlow == ControlFlowMode.Middle && card.ControlKind == ControlFlowKind.Menu)
        {
            // A menu case with its body: up to the next case or the end.
            var last = i;
            var nested = 0;
            for (var k = i + 1; k < actions.Count; k++)
            {
                var ak = actions[k];
                if (ak.GroupingIdentifier == a.GroupingIdentifier && nested == 0) break;
                if (ak.ControlFlow == ControlFlowMode.Start) nested++;
                if (ak.ControlFlow == ControlFlowMode.End) nested--;
                last = k;
            }
            return (i, last);
        }
        if (a.ControlFlow == ControlFlowMode.Middle) return (i, i);
        return ControlFlow.BlockRange(actions, i);
    }

    [RelayCommand]
    private void DeleteCard(ActionCardViewModel? card)
    {
        card ??= SelectedCard;
        if (card is null) return;
        _checkpointTaken = false;
        Checkpoint();
        var (first, last) = OperationRange(card);
        var group = card.Action.GroupingIdentifier;
        Current.Actions.RemoveRange(first, last - first + 1);
        if (card.IsMenuCase) SyncMenuItems(group);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(Math.Min(first, Current.Actions.Count - 1));
    }

    [RelayCommand]
    private void DuplicateCard(ActionCardViewModel? card)
    {
        card ??= SelectedCard;
        if (card is null || (card.IsMarker && !card.IsMenuCase)) return;
        _checkpointTaken = false;
        Checkpoint();
        var (first, last) = OperationRange(card);
        var copies = Current.Actions.Skip(first).Take(last - first + 1).Select(a => a.Clone()).ToList();

        // Fresh UUIDs, and fresh grouping identifiers for blocks that are copied whole.
        // A copied menu case keeps its menu's group so it stays inside that menu.
        var uuidMap = new Dictionary<string, string>();
        var groupMap = copies
            .Where(c => c.ControlFlow == ControlFlowMode.Start && c.GroupingIdentifier is not null)
            .Select(c => c.GroupingIdentifier!)
            .Distinct()
            .ToDictionary(g => g, _ => ActionInstance.NewUuid());
        foreach (var c in copies)
        {
            if (c.Uuid is { } u) uuidMap[u] = c.Uuid = ActionInstance.NewUuid();
            if (c.GroupingIdentifier is { } g && groupMap.TryGetValue(g, out var ng)) c.GroupingIdentifier = ng;
        }
        foreach (var c in copies)
            foreach (var key in c.Parameters.Keys.ToList())
                c.Parameters[key] = RemapOutputs(c.Parameters[key], uuidMap);

        Current.Actions.InsertRange(last + 1, copies);
        if (card.IsMenuCase) SyncMenuItems(card.Action.GroupingIdentifier);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(last + 1);
    }

    private static ParamValue RemapOutputs(ParamValue value, IReadOnlyDictionary<string, string> map) =>
        MapVariables(value, v => v.OutputUuid is { } u && map.TryGetValue(u, out var n) ? v with { OutputUuid = n } : v);

    /// <summary>Applies <paramref name="map"/> to every variable reference inside a parameter value.</summary>
    private static ParamValue MapVariables(ParamValue value, Func<VariableRef, VariableRef> map) => value switch
    {
        VariableValue v => new VariableValue(map(v.Variable)),
        TokenString t => t with { Attachments = t.Attachments.Select(a => a with { Variable = map(a.Variable) }).ToList() },
        ArrayValue a => new ArrayValue(a.Items.Select(i => MapVariables(i, map)).ToList()),
        DictValue d => new DictValue(d.Entries.Select(e => new KeyValuePair<string, ParamValue>(e.Key, MapVariables(e.Value, map))).ToList()),
        DictionaryFieldValue f => new DictionaryFieldValue(f.Items.Select(i =>
            i with { Key = (TokenString)MapVariables(i.Key, map), Value = MapVariables(i.Value, map) }).ToList()),
        WrappedValue w => w with { Value = MapVariables(w.Value, map) },
        _ => value,
    };

    /// <summary>
    /// Renames an action's output (magic variable), like "Rename" in the Shortcuts app: sets
    /// CustomOutputName and updates the name in every reference. Empty name restores the default.
    /// </summary>
    public void RenameOutput(ActionCardViewModel card, string? newName)
    {
        if (card.Action.Uuid is not { } uuid) return;
        newName = string.IsNullOrWhiteSpace(newName) ? null : newName.Trim();
        var shownName = newName ?? card.Definition?.Output ?? "Output";

        _checkpointTaken = false;
        Checkpoint();
        card.Action.SetOrRemove(ActionInstance.CustomOutputNameKey, newName is null ? null : new StringValue(newName));
        foreach (var action in Current.Actions)
            foreach (var key in action.Parameters.Keys.ToList())
                action.Parameters[key] = MapVariables(action.Parameters[key],
                    v => v.Kind == VariableKinds.ActionOutput && v.OutputUuid == uuid ? v with { OutputName = shownName } : v);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(card.Index);
        Status = L.F("Kimenet átnevezve: {0}", "Output renamed: {0}", shownName);
    }

    [RelayCommand]
    private void MoveUp(ActionCardViewModel? card) => MoveBy(card ?? SelectedCard, -1);

    [RelayCommand]
    private void MoveDown(ActionCardViewModel? card) => MoveBy(card ?? SelectedCard, +1);

    private void MoveBy(ActionCardViewModel? card, int direction)
    {
        if (card is null || card.IsMarker) return;
        var (first, last) = OperationRange(card);
        if (direction < 0)
        {
            if (first == 0) return;
            MoveBlock(card.Index, first - 1);
        }
        else
        {
            if (last >= Current.Actions.Count - 1) return;
            // Jump over the whole next block if the next card starts one.
            var next = Current.Actions[last + 1];
            var target = next.ControlFlow == ControlFlowMode.Start
                ? ControlFlow.BlockRange(Current.Actions, last + 1).Last + 1
                : last + 2;
            MoveBlock(card.Index, target);
        }
    }

    /// <summary>Moves the card's action (or whole block) so it is inserted before index <paramref name="target"/>.</summary>
    public void MoveBlock(int cardIndex, int target)
    {
        if (cardIndex < 0 || cardIndex >= Cards.Count) return;
        var card = Cards[cardIndex];
        if (card.IsMarker && !card.IsMenuCase) return;
        var (first, last) = OperationRange(card);
        if (target >= first && target <= last + 1) return;

        var wasValid = ControlFlow.Validate(Current.Actions) is null;
        var snapshot = TakeSnapshot();
        var before = Current.Actions.ToList();
        var moved = Current.Actions.GetRange(first, last - first + 1);
        Current.Actions.RemoveRange(first, moved.Count);
        if (target > last) target -= moved.Count;
        target = Math.Clamp(target, 0, Current.Actions.Count);
        Current.Actions.InsertRange(target, moved);

        // A move must not break the block structure (e.g. a menu item dragged out of its menu).
        if (wasValid && ControlFlow.Validate(Current.Actions) is not null)
        {
            Current.Actions.Clear();
            Current.Actions.AddRange(before);
            Status = L.T("Ide nem mozgatható: elrontaná a blokkok szerkezetét.", "Cannot move there: it would break the block structure.");
            return;
        }

        _checkpointTaken = false;
        _undo.Push(snapshot);
        _redo.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        if (card.IsMenuCase) SyncMenuItems(card.Action.GroupingIdentifier);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(target);
    }

    [RelayCommand]
    private void AddMenuCase(ActionCardViewModel? card)
    {
        if (card is null || card.ControlKind != ControlFlowKind.Menu) return;
        var group = card.Action.GroupingIdentifier;
        var endIndex = Current.Actions.FindIndex(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.End);
        if (endIndex < 0) return;
        _checkpointTaken = false;
        Checkpoint();
        var count = Current.Actions.Count(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.Middle);
        var middle = new ActionInstance(ControlFlow.MenuId) { GroupingIdentifier = group, ControlFlow = ControlFlowMode.Middle };
        middle.Parameters["WFMenuItemTitle"] = new StringValue(L.T($"Menüpont {count + 1}", $"Menu item {count + 1}"));
        Current.Actions.Insert(endIndex, middle);
        SyncMenuItems(group);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(endIndex);
    }

    [RelayCommand]
    private void AddElse(ActionCardViewModel? card)
    {
        if (card is null || card.ControlKind != ControlFlowKind.If) return;
        var group = card.Action.GroupingIdentifier;
        if (Current.Actions.Any(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.Middle)) return;
        var endIndex = Current.Actions.FindIndex(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.End);
        if (endIndex < 0) return;
        _checkpointTaken = false;
        Checkpoint();
        Current.Actions.Insert(endIndex, new ActionInstance(ControlFlow.IfId) { GroupingIdentifier = group, ControlFlow = ControlFlowMode.Middle });
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(endIndex);
    }

    public bool CanAddElse(ActionCardViewModel card) =>
        card.IsIfStart && !Current.Actions.Any(a => a.GroupingIdentifier == card.Action.GroupingIdentifier && a.ControlFlow == ControlFlowMode.Middle);

    // ------------------------------------------------------------------ DSL view

    /// <summary>Raised when the model changed and the DSL text should be regenerated.</summary>
    public event EventHandler? DslSourceChanged;

    [ObservableProperty] private bool _isDslTabActive;
    [ObservableProperty] private string? _dslError;
    [ObservableProperty] private int _dslErrorOffset = -1;
    [ObservableProperty] private int _dslErrorLength;
    [ObservableProperty] private int _dslErrorLine;
    [ObservableProperty] private int _dslErrorColumn;

    private void SetDslError(DslException ex)
    {
        DslErrorLine = ex.Pos.Line;
        DslErrorColumn = ex.Pos.Column;
        DslErrorLength = ex.Length;
        DslErrorOffset = ex.Offset;
        DslError = ex.ToString();
    }

    /// <summary>DSL text being edited (set by the editor); null when it matches the model.</summary>
    public string? PendingDsl { get; private set; }

    public string CurrentDsl => DslPrinter.Print(Current);

    /// <summary>Called by the editor on text changes: validates without applying.</summary>
    public void OnDslEdited(string text)
    {
        PendingDsl = text;
        try
        {
            DslParser.Parse(text);
            DslError = null;
            DslErrorOffset = -1;
            Status = L.T("A szöveg érvényes.", "The text is valid.");
        }
        catch (DslException ex)
        {
            SetDslError(ex);
        }
    }

    /// <summary>Applies pending DSL edits to the model. Returns false if the text has errors.</summary>
    public bool ApplyDslIfNeeded()
    {
        if (PendingDsl is null) return true;
        Shortcut parsed;
        try
        {
            parsed = DslParser.Parse(PendingDsl);
        }
        catch (DslException ex)
        {
            SetDslError(ex);
            return false;
        }

        _checkpointTaken = false;
        Checkpoint();
        _checkpointTaken = false;
        Current = parsed;
        PendingDsl = null;
        DslError = null;
        IsDirty = true;
        LoadSettingsPanel();
        RebuildCards();
        return true;
    }

    /// <summary>The editor was just reloaded from the model: nothing pending.</summary>
    public void DiscardDslEditsSilently()
    {
        PendingDsl = null;
        DslError = null;
        DslErrorLine = 0;
    }

    public void DiscardDslEdits()
    {
        PendingDsl = null;
        DslError = null;
        DslSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ApplyDsl()
    {
        if (ApplyDslIfNeeded()) Status = L.T("Szöveg alkalmazva.", "Text applied.");
        else Status = L.T("A szövegben hiba van: ", "The text has an error: ") + DslError;
    }

    [RelayCommand]
    private void FormatDsl()
    {
        if (!ApplyDslIfNeeded()) return;
        DslSourceChanged?.Invoke(this, EventArgs.Empty);
        Status = L.T("Szöveg újraformázva.", "Text reformatted.");
    }

    // ------------------------------------------------------------------ files

    private bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        var answer = _dialogs.AskYesNoCancel(L.T("Nem mentett változások", "Unsaved changes"), L.T("Mented a módosításokat?", "Save your changes?"));
        if (answer is null) return false;
        return answer == false || Save();
    }

    public bool CanClose() => ApplyDslIfNeeded() || _dialogs.Confirm(L.T("Hibás szöveg", "Invalid text"),
        L.T("A szöveges nézetben hiba van, a módosítások elvesznek. Kilépsz?", "The text view has an error; the changes will be lost. Quit anyway?")) ? ConfirmDiscard() : false;

    [RelayCommand]
    private void New()
    {
        if (!ApplyDslIfNeeded() && !_dialogs.Confirm(L.T("Hibás szöveg", "Invalid text"), L.T("A szöveges módosítások elvesznek. Folytatod?", "The text changes will be lost. Continue?"))) return;
        PendingDsl = null;
        if (!ConfirmDiscard()) return;
        LoadShortcut(new Shortcut(), null);
        Status = L.T("Új parancs.", "New shortcut.");
    }

    /// <summary>A fresh list each time, so the menu re-reads the (localized) names.</summary>
    public IReadOnlyList<ShortcutTemplate> Templates => ShortcutTemplates.All.ToList();

    [RelayCommand]
    private void NewFromTemplate(ShortcutTemplate? template)
    {
        if (template is null || !ConfirmDiscardIncludingDsl()) return;
        LoadShortcut(DslParser.Parse(template.Source), null);
        Status = L.F("Új parancs sablonból: {0}", "New shortcut from template: {0}", template.Name);
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscardIncludingDsl()) return;
        var path = _dialogs.OpenFile(ShortcutDocument.OpenFilter);
        if (path is not null) OpenPath(path);
    }

    private bool ConfirmDiscardIncludingDsl()
    {
        if (!ApplyDslIfNeeded() && !_dialogs.Confirm(L.T("Hibás szöveg", "Invalid text"), L.T("A szöveges módosítások elvesznek. Folytatod?", "The text changes will be lost. Continue?"))) return false;
        PendingDsl = null;
        return ConfirmDiscard();
    }

    public void OpenPath(string path)
    {
        try
        {
            var shortcut = ShortcutDocument.Open(path);
            var isProject = Path.GetExtension(path).Equals(ShortcutDocument.ProjectExtension, StringComparison.OrdinalIgnoreCase);
            LoadShortcut(shortcut, isProject ? path : null);
            Settings.AddRecent(path);
            Settings.Save();
            OnPropertyChanged(nameof(RecentFiles));
            Status = L.T($"Megnyitva: {path}", $"Opened: {path}");
            if (!isProject) Status += L.T(" (importálva; mentéskor .sfdsl fájl készül, exporttal .shortcut)", " (imported; Save creates a .sfdsl file, Export a .shortcut)");
        }
        catch (Exception ex) when (ex is IOException or FormatException or DslException or InvalidDataException or UnauthorizedAccessException)
        {
            _dialogs.Error(L.T("Megnyitás sikertelen", "Could not open the file"), ex.Message);
        }
    }

    public IReadOnlyList<string> RecentFiles => Settings.RecentFiles;

    [RelayCommand]
    private void OpenRecent(string? path)
    {
        if (path is null || !ConfirmDiscardIncludingDsl()) return;
        OpenPath(path);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveFile() => Save();

    private static bool CanSave() => true;

    [RelayCommand]
    private void SaveFileAs() => SaveAs();

    public bool Save() => FilePath is null ? SaveAs() : SaveTo(FilePath);

    public bool SaveAs()
    {
        var path = _dialogs.SaveFile(ShortcutDocument.SaveFilter, SafeFileName(Current.Name) + ShortcutDocument.ProjectExtension);
        return path is not null && SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        if (!ApplyDslIfNeeded())
        {
            _dialogs.Error(L.T("Mentés", "Save"), L.T("A szöveges nézetben hiba van:\n", "The text view has an error:\n") + DslError);
            return false;
        }
        try
        {
            ShortcutDocument.SaveProject(Current, path);
            FilePath = path;
            IsDirty = false;
            Settings.AddRecent(path);
            Settings.Save();
            OnPropertyChanged(nameof(RecentFiles));
            Status = L.T($"Mentve: {path}", $"Saved: {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error(L.T("Mentés sikertelen", "Save failed"), ex.Message);
            return false;
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "parancs" : cleaned;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!ApplyDslIfNeeded())
        {
            _dialogs.Error(L.T("Exportálás", "Export"), L.T("A szöveges nézetben hiba van:\n", "The text view has an error:\n") + DslError);
            return;
        }
        if (ControlFlow.Validate(Current.Actions) is { } problem &&
            !_dialogs.Confirm(L.T("Szerkezeti hiba", "Structure error"), problem + L.T("\n\nÍgy is exportálod?", "\n\nExport anyway?")))
            return;

        var path = _dialogs.SaveFile(ShortcutDocument.ExportFilter, SafeFileName(Current.Name) + ".shortcut");
        if (path is null) return;

        try
        {
            IsBusy = true;
            if (Path.GetExtension(path).Equals(".plist", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(path, PlistSerializer.WriteXml(Current));
                Status = L.T($"XML plist exportálva: {path}", $"XML plist exported: {path}");
                return;
            }

            // One-time offer of free online signing, so nothing is sent anywhere without consent.
            if (Settings.Signer == SignerKind.Unsigned && !Settings.ShortcutyOfferShown)
            {
                Settings.ShortcutyOfferShown = true;
                if (_dialogs.Confirm(L.T("Ingyenes aláírás", "Free signing"),
                        L.T("Az iPhone csak aláírt parancsot importál. Aláírassam most ingyen a Shortcuty online szolgáltatással?\n\n" +
                            "A parancs tartalma a Shortcuty szerverére kerül (jelszót, személyes adatot ne küldj így). " +
                            "Később az Eszközök › Beállítások menüben módosítható.",
                            "iPhone only imports signed shortcuts. Sign it now for free with the Shortcuty online service?\n\n" +
                            "The shortcut's content is sent to Shortcuty's server (don't send passwords or personal data this way). " +
                            "You can change this later in Tools › Settings.")))
                    Settings.Signer = SignerKind.Shortcuty;
                Settings.Save();
            }

            var signer = Settings.CreateSigner();
            Status = L.T($"Exportálás: {signer.DisplayName}…", $"Exporting: {signer.DisplayName}…");
            var bytes = await signer.SignAsync(ShortcutDocument.UnsignedBytes(Current), Settings.SigningMode, Current.Name);
            await File.WriteAllBytesAsync(path, bytes);

            if (signer.ProducesSignedFile)
            {
                Status = L.T($"Aláírt shortcut exportálva: {path}", $"Signed shortcut exported: {path}");
                _dialogs.Info(L.T("Kész", "Done"), L.T("Az aláírt shortcut elkészült. AirDroppal, e-mailben vagy iCloud Drive-on át ", "The signed shortcut is ready. Open it on iPhone, iPad or Mac via AirDrop, ") +
                                      L.T("megnyitható iPhone-on, iPaden és Macen.", "email or iCloud Drive."));
            }
            else
            {
                Status = L.T($"Aláíratlan shortcut exportálva: {path}", $"Unsigned shortcut exported: {path}");
                _dialogs.Info(L.T("Aláírás szükséges", "Signing required"),
                    L.T("A fájl aláírás nélkül készült. iOS 15 óta az iPhone csak aláírt parancsot importál.\n\n", "The file is unsigned. Since iOS 15, iPhone only imports signed shortcuts.\n\n") +
                    L.T("Aláírás Macen (Terminál):\n", "To sign on a Mac (Terminal):\n") +
                    L.T($"  shortcuts sign --mode anyone --input \"{Path.GetFileName(path)}\" --output \"alairt.shortcut\"\n\n", $"  shortcuts sign --mode anyone --input \"{Path.GetFileName(path)}\" --output \"signed.shortcut\"\n\n") +
                    L.T("Vagy állíts be Mac-es aláírást SSH-n keresztül: Eszközök › Beállítások.\n\n", "Or set up signing on a Mac over SSH: Tools › Settings.\n\n") +
                    L.T("Mac nélkül, ingyen: Eszközök › Beállítások › „Ingyenes online aláírás – Shortcuty”, vagy Fájl › Exportálás iPhone-ra.",
                        "Without a Mac, for free: Tools › Settings › \"Free online signing – Shortcuty\", or File › Export for iPhone."));
            }
        }
        catch (SigningException ex)
        {
            _dialogs.Error(L.T("Aláírás sikertelen", "Signing failed"), ex.Message);
            Status = L.T("Aláírás sikertelen.", "Signing failed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error(L.T("Exportálás sikertelen", "Export failed"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------------ find & replace

    [ObservableProperty] private bool _isFindPanelOpen;
    [ObservableProperty] private string _findText = "";
    [ObservableProperty] private string _replaceText = "";
    [ObservableProperty] private bool _findMatchCase;
    [ObservableProperty] private string? _findStatus;
    [ObservableProperty] private string? _renameFrom;
    [ObservableProperty] private string _renameTo = "";

    public IReadOnlyList<string> NamedVariableNames => ShortcutRefactor.NamedVariables(Current);

    /// <summary>Asks the view to bring a card into view (index).</summary>
    public event EventHandler<int>? CardFocusRequested;

    partial void OnFindTextChanged(string value) => FindStatus = null;

    [RelayCommand]
    private void ToggleFindPanel()
    {
        IsFindPanelOpen = !IsFindPanelOpen;
        if (IsFindPanelOpen)
        {
            OnPropertyChanged(nameof(NamedVariableNames));
            RenameFrom ??= NamedVariableNames.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void CloseFindPanel() => IsFindPanelOpen = false;

    private IReadOnlyList<int> Matches() => ShortcutRefactor.FindActions(Current, FindText, FindMatchCase,
        a => ActionCatalog.Default.ById(a.Identifier)?.Name);

    [RelayCommand]
    private void FindNext()
    {
        if (!ApplyDslIfNeeded() || string.IsNullOrEmpty(FindText)) return;
        var matches = Matches();
        if (matches.Count == 0)
        {
            FindStatus = L.T("Nincs találat.", "No matches.");
            return;
        }
        var from = SelectedCard?.Index ?? -1;
        var next = matches.FirstOrDefault(i => i > from, matches[0]);
        var card = Cards[next];
        card.IsCollapsed = false;
        SelectedCard = card;
        CardFocusRequested?.Invoke(this, next);
        FindStatus = L.T($"{matches.ToList().IndexOf(next) + 1}. / {matches.Count} találat", $"Match {matches.ToList().IndexOf(next) + 1} of {matches.Count}");
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (!ApplyDslIfNeeded() || string.IsNullOrEmpty(FindText)) return;
        _checkpointTaken = false;
        var snapshot = TakeSnapshot();
        var count = ShortcutRefactor.ReplaceText(Current, FindText, ReplaceText, FindMatchCase);
        if (count == 0)
        {
            FindStatus = L.T("Nincs mit cserélni.", "Nothing to replace.");
            return;
        }
        _undo.Push(snapshot);
        _redo.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        MarkDirty();
        RebuildCards(SelectedCard?.Index);
        FindStatus = L.T($"{count} csere.", $"{count} replaced.");
        Status = FindStatus;
    }

    [RelayCommand]
    private void RenameVariable()
    {
        if (!ApplyDslIfNeeded() || string.IsNullOrEmpty(RenameFrom) || string.IsNullOrWhiteSpace(RenameTo)) return;
        var newName = RenameTo.Trim();
        if (NamedVariableNames.Contains(newName) && newName != RenameFrom &&
            !_dialogs.Confirm(L.T("Változó átnevezése", "Rename variable"),
                L.T($"Már van „{newName}” nevű változó; a kettő összeolvad. Folytatod?",
                    $"A variable named \"{newName}\" already exists; the two will be merged. Continue?")))
            return;
        _checkpointTaken = false;
        var snapshot = TakeSnapshot();
        var count = ShortcutRefactor.RenameVariable(Current, RenameFrom, newName);
        if (count == 0) return;
        _undo.Push(snapshot);
        _redo.Clear();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        MarkDirty();
        RebuildCards(SelectedCard?.Index);
        FindStatus = L.T($"„{RenameFrom}” → „{newName}”: {count} helyen.", $"\"{RenameFrom}\" → \"{newName}\": {count} places.");
        OnPropertyChanged(nameof(NamedVariableNames));
        RenameFrom = newName;
        RenameTo = "";
    }

    public const string SourceHelperUrl = "https://routinehub.co/shortcut/10060/";

    /// <summary>
    /// Free route without a Mac: saves "Name.plist" for the "Shortcut Source Helper" shortcut
    /// (RoutineHub), which signs it remotely on the iPhone and imports it.
    /// </summary>
    [RelayCommand]
    private async Task ExportForIPhoneAsync()
    {
        if (!ApplyDslIfNeeded())
        {
            _dialogs.Error(L.T("Exportálás", "Export"), L.T("A szöveges nézetben hiba van:\n", "The text view has an error:\n") + DslError);
            return;
        }
        if (ControlFlow.Validate(Current.Actions) is { } problem &&
            !_dialogs.Confirm(L.T("Szerkezeti hiba", "Structure error"), problem + L.T("\n\nÍgy is exportálod?", "\n\nExport anyway?")))
            return;

        // The helper names the imported shortcut after the file name.
        var path = _dialogs.SaveFile("Plist (*.plist)|*.plist", SafeFileName(Current.Name) + ".plist");
        if (path is null) return;
        try
        {
            await File.WriteAllTextAsync(path, PlistSerializer.WriteXml(Current));
            Status = L.T($"iPhone-os plist exportálva: {path}", $"Plist for iPhone exported: {path}");
            _dialogs.Info(L.T("Küldés iPhone-ra – ingyenes út Mac nélkül", "Sending to iPhone – free route without a Mac"),
                L.T("A fájl elkészült. Így kerül fel a telefonra:\n\n" +
                    "1. iPhone-on telepítsd egyszer a „Shortcut Source Helper” parancsot (ingyenes, RoutineHub):\n" +
                    $"   {SourceHelperUrl}\n" +
                    "2. Juttasd át a .plist fájlt a telefonra (AirDrop, iCloud Drive, e-mail, OneDrive…).\n" +
                    "3. A Fájlok appban nyisd meg a megosztás menüt, és válaszd a Shortcut Source Helpert.\n" +
                    "4. A helper aláírja (Remote Sign) és importálja a parancsot – a neve a fájlnév lesz.\n\n" +
                    "A távoli aláírást a helper szolgáltatása végzi; ha épp nem elérhető, próbáld később.",
                    "The file is ready. To get it onto your phone:\n\n" +
                    "1. On the iPhone, install the \"Shortcut Source Helper\" shortcut once (free, RoutineHub):\n" +
                    $"   {SourceHelperUrl}\n" +
                    "2. Transfer the .plist file to the phone (AirDrop, iCloud Drive, email, OneDrive…).\n" +
                    "3. In the Files app open the share menu and choose Shortcut Source Helper.\n" +
                    "4. The helper signs it (Remote Sign) and imports the shortcut, named after the file.\n\n" +
                    "Remote signing is done by the helper's service; if it is unavailable, try again later."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.Error(L.T("Exportálás sikertelen", "Export failed"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportICloudAsync()
    {
        if (!ConfirmDiscardIncludingDsl()) return;
        var link = _dialogs.Prompt(L.T("Importálás iCloud linkről", "Import from iCloud link"),
            L.T("Illeszd be a megosztott parancs linkjét (https://www.icloud.com/shortcuts/…):", "Paste the link of the shared shortcut (https://www.icloud.com/shortcuts/…):"));
        if (string.IsNullOrWhiteSpace(link)) return;
        try
        {
            IsBusy = true;
            Status = L.T("Letöltés iCloudról…", "Downloading from iCloud…");
            var shortcut = await ICloudImporter.ImportAsync(link.Trim());
            LoadShortcut(shortcut, null);
            IsDirty = true;
            Status = L.T($"Importálva iCloudról: {shortcut.Name}", $"Imported from iCloud: {shortcut.Name}");
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException or InvalidOperationException
                                       or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException
                                       or TaskCanceledException)
        {
            _dialogs.Error(L.T("Importálás sikertelen", "Import failed"), ex.Message);
            Status = L.T("Importálás sikertelen.", "Import failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (_dialogs.EditSettings(Settings))
        {
            Settings.Save();
            Status = L.T("Beállítások mentve.", "Settings saved.");
        }
    }

    [RelayCommand]
    private void CopyPlistXml()
    {
        if (!ApplyDslIfNeeded()) return;
        System.Windows.Clipboard.SetText(PlistSerializer.WriteXml(Current));
        Status = L.T("A plist XML a vágólapra került.", "The plist XML was copied to the clipboard.");
    }
}

public sealed record ColorOption(string Name, long Value)
{
    public System.Windows.Media.Brush Brush
    {
        get
        {
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
                (byte)((Value >> 24) & 0xFF), (byte)((Value >> 16) & 0xFF), (byte)((Value >> 8) & 0xFF)));
            brush.Freeze();
            return brush;
        }
    }
}

public sealed partial class CheckOption(string key, string label, Action onChanged) : ObservableObject
{
    private bool _silent;

    public string Key { get; } = key;

    [ObservableProperty] private string _label = label;

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_silent) onChanged();
    }

    public void SetSilently(bool value)
    {
        _silent = true;
        IsChecked = value;
        _silent = false;
    }
}
