using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Dsl;

namespace ShortcutForge.Editor;

/// <summary>
/// The editing state of one shortcut, independent of any UI framework: the action library,
/// the visual editor's cards, undo / redo, the text (DSL) view and the shortcut settings.
/// Behaviour follows the Windows app's MainViewModel. File dialogs, signing and other
/// platform services are added by the hosting app in a subclass.
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    private const int MaxUndo = 200;

    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private bool _checkpointTaken;
    private bool _loadingSettings;

    public EditorViewModel()
    {
        InputClassOptions = KnownInputClasses.Select(c => new CheckOption(c.Key, c.Label, OnInputClassesChanged)).ToList();
        TypeOptions = KnownTypes.Select(c => new CheckOption(c.Key, c.Label, OnTypesChanged)).ToList();
        RefreshLibrary();
        L.Changed += (_, _) => OnLanguageChanged();
        LoadShortcut(DslParser.Parse(ShortcutDocument.ExampleSource), null);
    }

    public Shortcut Current { get; private set; } = new();

    public NameScope Scope { get; private set; } = new();

    public ObservableCollection<ActionCardViewModel> Cards { get; } = [];

    /// <summary>Actions whose cards are collapsed (kept across card rebuilds).</summary>
    public HashSet<ActionInstance> CollapsedActions { get; } = new(ReferenceEqualityComparer.Instance);

    [ObservableProperty] private ActionCardViewModel? _selectedCard;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = L.T("Kész.", "Ready.");
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _structureWarning;

    public string WindowTitle => $"{(IsDirty ? "● " : "")}{Current.Name} – ShortcutForge";

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));

    public IReadOnlyList<string> VariableSuggestions { get; private set; } = [];

    public string VersionText => $"v{Core.AppInfo.Version}";

    public string ActionCountText => L.T($"{Current.Actions.Count} akció", $"{Current.Actions.Count} actions");

    [RelayCommand]
    private void CollapseAll(string? collapse)
    {
        var value = collapse != "false";
        foreach (var card in Cards.Where(c => c.CanCollapse)) card.IsCollapsed = value;
    }

    /// <summary>Re-evaluates every text that comes from code rather than from the view.</summary>
    protected virtual void OnLanguageChanged()
    {
        foreach (var o in InputClassOptions) o.Label = KnownInputClasses.First(k => k.Key == o.Key).Label;
        foreach (var o in TypeOptions) o.Label = KnownTypes.First(k => k.Key == o.Key).Label;
        RefreshLibrary();
        RebuildCards(SelectedCard?.Index);
        OnPropertyChanged(nameof(ActionCountText));
        if (DslError is not null && PendingDsl is not null) OnDslEdited(PendingDsl);
        Status = L.T("Nyelv: magyar", "Language: English");
    }

    // ------------------------------------------------------------------ library

    /// <summary>The action library grouped by (localized) category, filtered by <see cref="SearchText"/>.</summary>
    public ObservableCollection<LibraryGroup> Library { get; } = [];

    [ObservableProperty] private string _searchText = "";

    partial void OnSearchTextChanged(string value) => RefreshLibrary();

    public int CatalogCount => ActionCatalog.Default.Actions.Count;

    public static bool MatchesSearch(ActionDefinition a, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var q = search.Trim();
        return a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.Dsl.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               a.DisplayCategory.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               (a.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (a.DescriptionEn?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RefreshLibrary()
    {
        Library.Clear();
        foreach (var group in ActionCatalog.Default.Actions
                     .Where(a => MatchesSearch(a, SearchText))
                     .GroupBy(a => a.DisplayCategory))
            Library.Add(new LibraryGroup(group.Key, group.Select(a => new LibraryItem(a)).ToList()));
        OnPropertyChanged(nameof(LibraryIsEmpty));
    }

    public bool LibraryIsEmpty => Library.Count == 0;

    /// <summary>Adds the first action matching the search (Enter in the search box).</summary>
    [RelayCommand]
    private void AddFirstMatch()
    {
        if (Library.FirstOrDefault()?.Items.FirstOrDefault() is { } first) AddAction(first.Definition);
    }

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
        _checkpointTaken = false;
    }

    partial void OnSelectedColorChanged(ColorOption? value)
    {
        if (_loadingSettings || value is null) return;
        Checkpoint();
        Current.Icon = Current.Icon with { StartColor = value.Value };
        MarkDirty();
        _checkpointTaken = false;
    }

    partial void OnGlyphNumberChanged(long value)
    {
        if (_loadingSettings) return;
        Checkpoint();
        Current.Icon = Current.Icon with { GlyphNumber = value };
        MarkDirty();
        _checkpointTaken = false;
    }

    private void OnInputClassesChanged()
    {
        if (_loadingSettings) return;
        Checkpoint();
        var known = InputClassOptions.Select(o => o.Key).ToHashSet();
        Current.InputClasses = Current.InputClasses.Where(c => !known.Contains(c))
            .Concat(InputClassOptions.Where(o => o.IsChecked).Select(o => o.Key)).ToList();
        MarkDirty();
        _checkpointTaken = false;
    }

    private void OnTypesChanged()
    {
        if (_loadingSettings) return;
        Checkpoint();
        var known = TypeOptions.Select(o => o.Key).ToHashSet();
        Current.Types = Current.Types.Where(c => !known.Contains(c))
            .Concat(TypeOptions.Where(o => o.IsChecked).Select(o => o.Key)).ToList();
        MarkDirty();
        _checkpointTaken = false;
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

    /// <summary>Replaces the edited shortcut (new / open); clears undo and the dirty flag.</summary>
    public void LoadShortcut(Shortcut shortcut, string? path)
    {
        Current = shortcut;
        FilePath = path;
        PendingDsl = null;
        DslError = null;
        _undo.Clear();
        _redo.Clear();
        _checkpointTaken = false;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        IsDirty = false;
        LoadSettingsPanel();
        RebuildCards();
        DslSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised after the cards were recreated, with the index of the card to bring into view.</summary>
    public event EventHandler<int?>? CardsRebuilt;

    /// <summary>Recreates all cards from the model (after structural changes).</summary>
    public void RebuildCards(int? select = null)
    {
        RefreshScope();
        var indent = ControlFlow.ComputeIndent(Current.Actions);
        SelectedCard = null;
        Cards.Clear();
        for (var i = 0; i < Current.Actions.Count; i++)
            Cards.Add(new ActionCardViewModel(this, Current.Actions[i], i, indent[i]));
        foreach (var c in Cards) c.Refresh();
        if (select is { } s && s >= 0 && s < Cards.Count) SelectedCard = Cards[s];
        OnPropertyChanged(nameof(ActionCountText));
        StructureWarning = ControlFlow.Validate(Current.Actions);
        CardsRebuilt?.Invoke(this, select);
    }

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

    protected void MarkDirty()
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
        if (_checkpointTaken) return;
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

    /// <summary>Starts a new undo step (the next change gets its own checkpoint).</summary>
    private void NewUndoStep()
    {
        _checkpointTaken = false;
        Checkpoint();
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
        if (!ApplyDslIfNeeded() || _undo.Count == 0) return;
        _redo.Push(TakeSnapshot());
        Restore(_undo.Pop());
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public bool CanUndo() => _undo.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (!ApplyDslIfNeeded() || _redo.Count == 0) return;
        _undo.Push(TakeSnapshot());
        Restore(_redo.Pop());
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public bool CanRedo() => _redo.Count > 0;

    // ------------------------------------------------------------------ editing commands

    [RelayCommand]
    public void AddAction(ActionDefinition? definition) => InsertAction(definition, null);

    public void InsertAction(ActionDefinition? definition, int? index)
    {
        if (definition is null || !ApplyDslIfNeeded()) return;
        NewUndoStep();
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
        if (card is null || !ApplyDslIfNeeded() || card.Index >= Current.Actions.Count) return;
        NewUndoStep();
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
        if (card is null || (card.IsMarker && !card.IsMenuCase) || !ApplyDslIfNeeded()) return;
        NewUndoStep();
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
                c.Parameters[key] = MapVariables(c.Parameters[key],
                    v => v.OutputUuid is { } id && uuidMap.TryGetValue(id, out var n) ? v with { OutputUuid = n } : v);

        Current.Actions.InsertRange(last + 1, copies);
        if (card.IsMenuCase) SyncMenuItems(card.Action.GroupingIdentifier);
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(last + 1);
    }

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

    [RelayCommand]
    private void MoveUp(ActionCardViewModel? card) => MoveBy(card ?? SelectedCard, -1);

    [RelayCommand]
    private void MoveDown(ActionCardViewModel? card) => MoveBy(card ?? SelectedCard, +1);

    private void MoveBy(ActionCardViewModel? card, int direction)
    {
        if (card is null || !ApplyDslIfNeeded()) return;
        if (card.IsMarker && !card.IsMenuCase) return;
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

        // A move must not break the block structure (e.g. a menu item moved out of its menu).
        if (wasValid && ControlFlow.Validate(Current.Actions) is not null)
        {
            Current.Actions.Clear();
            Current.Actions.AddRange(before);
            Status = L.T("Ide nem mozgatható: elrontaná a blokkok szerkezetét.", "Cannot move there: it would break the block structure.");
            return;
        }

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
        if (card is null || card.ControlKind != ControlFlowKind.Menu || !ApplyDslIfNeeded()) return;
        var group = card.Action.GroupingIdentifier;
        var endIndex = Current.Actions.FindIndex(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.End);
        if (endIndex < 0) return;
        NewUndoStep();
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
        if (card is null || card.ControlKind != ControlFlowKind.If || !ApplyDslIfNeeded()) return;
        var group = card.Action.GroupingIdentifier;
        if (Current.Actions.Any(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.Middle))
        {
            Status = L.T("Ennek a feltételnek már van „Egyébként” ága.", "This condition already has an \"Otherwise\" branch.");
            return;
        }
        var endIndex = Current.Actions.FindIndex(a => a.GroupingIdentifier == group && a.ControlFlow == ControlFlowMode.End);
        if (endIndex < 0) return;
        NewUndoStep();
        Current.Actions.Insert(endIndex, new ActionInstance(ControlFlow.IfId) { GroupingIdentifier = group, ControlFlow = ControlFlowMode.Middle });
        _checkpointTaken = false;
        MarkDirty();
        RebuildCards(endIndex);
    }

    // ------------------------------------------------------------------ DSL view

    /// <summary>Raised when the model changed and the DSL text should be regenerated.</summary>
    public event EventHandler? DslSourceChanged;

    [ObservableProperty] private string? _dslError;
    [ObservableProperty] private int _dslErrorLine;
    [ObservableProperty] private int _dslErrorColumn;
    [ObservableProperty] private int _dslErrorLength;

    private void SetDslError(DslException ex)
    {
        DslErrorLine = ex.Pos.Line;
        DslErrorColumn = ex.Pos.Column;
        DslErrorLength = ex.Length;
        DslError = ex.Pos.Line > 0
            ? L.T($"{ex.Pos.Line}. sor, {ex.Pos.Column}. oszlop: ", $"Line {ex.Pos.Line}, column {ex.Pos.Column}: ") + ex.Message
            : ex.Message;
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
            DslErrorLine = 0;
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

        NewUndoStep();
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

    /// <summary>Throws away invalid text edits and reloads the text from the model.</summary>
    public void DiscardDslEdits()
    {
        DiscardDslEditsSilently();
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
        if (!ApplyDslIfNeeded())
        {
            Status = L.T("A szövegben hiba van: ", "The text has an error: ") + DslError;
            return;
        }
        DslSourceChanged?.Invoke(this, EventArgs.Empty);
        Status = L.T("Szöveg újraformázva.", "Text reformatted.");
    }
}

/// <summary>A category of the action library.</summary>
public sealed record LibraryGroup(string Name, IReadOnlyList<LibraryItem> Items);

/// <summary>An action in the library, with its display colour and badge.</summary>
public sealed record LibraryItem(ActionDefinition Definition)
{
    public string Name => Definition.Name;
    public string Dsl => Definition.Dsl;
    public string? Description => Definition.DisplayDescription;
    public string Color => CategoryStyle.ColorOf(Definition);
    public string Badge => CategoryStyle.BadgeOf(Definition);
}

/// <summary>An icon colour offered by the Shortcuts app.</summary>
public sealed record ColorOption(string Name, long Value)
{
    /// <summary>"#RRGGBB" of the packed RGBA value.</summary>
    public string Hex => $"#{(Value >> 24) & 0xFF:X2}{(Value >> 16) & 0xFF:X2}{(Value >> 8) & 0xFF:X2}";

    public override string ToString() => Name;
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
