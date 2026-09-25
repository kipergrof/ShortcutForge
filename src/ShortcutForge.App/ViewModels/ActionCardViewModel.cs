using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShortcutForge.App.Services;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;
using ShortcutForge.Dsl;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.ViewModels;

/// <summary>An action in the visual editor (one row of the list, like in the Shortcuts app).</summary>
public sealed partial class ActionCardViewModel : ObservableObject
{
    private static readonly HashSet<string> HiddenKeys =
        [ActionInstance.UuidKey, ActionInstance.GroupingKey, ActionInstance.ControlFlowKey, ActionInstance.CustomOutputNameKey];

    private static readonly HashSet<string> IfConditionKeys =
        ["WFInput", "WFCondition", "WFConditionalActionString", "WFNumberValue", "WFAnotherNumber"];

    private bool _loading;

    public ActionCardViewModel(MainViewModel main, ActionInstance action, int index, int indent)
    {
        Main = main;
        Action = action;
        Index = index;
        Indent = indent;
        Definition = ActionCatalog.Default.ById(action.Identifier);
        BuildParams();
        LoadSpecial();
    }

    public MainViewModel Main { get; }
    public ActionInstance Action { get; }
    public ActionDefinition? Definition { get; }
    public int Index { get; }
    public int Indent { get; }

    public double IndentWidth => Indent * 28;

    public ObservableCollection<ParamViewModel> Params { get; } = [];

    public ControlFlowMode? Mode => Action.ControlFlow;
    public ControlFlowKind ControlKind => Definition?.Control ?? ControlFlowKind.None;

    public bool IsBlockStart => Mode == ControlFlowMode.Start;
    public bool IsMarker => Mode is ControlFlowMode.Middle or ControlFlowMode.End;
    public bool IsIfStart => ControlKind == ControlFlowKind.If && IsBlockStart;
    public bool IsMenuStart => ControlKind == ControlFlowKind.Menu && IsBlockStart;
    public bool IsMenuCase => ControlKind == ControlFlowKind.Menu && Mode == ControlFlowMode.Middle;
    public bool IsUnknown => Definition is null;
    public bool HasParams => Params.Count > 0;

    public string Title => (ControlKind, Mode) switch
    {
        (ControlFlowKind.If, ControlFlowMode.Start) => L.T("Ha", "If"),
        (ControlFlowKind.If, ControlFlowMode.Middle) => L.T("Egyébként", "Otherwise"),
        (ControlFlowKind.If, ControlFlowMode.End) => L.T("Ha vége", "End If"),
        (ControlFlowKind.Repeat, ControlFlowMode.Start) => L.T("Ismétlés", "Repeat"),
        (ControlFlowKind.Repeat, ControlFlowMode.End) => L.T("Ismétlés vége", "End Repeat"),
        (ControlFlowKind.RepeatEach, ControlFlowMode.Start) => L.T("Ismétlés minden elemre", "Repeat with Each"),
        (ControlFlowKind.RepeatEach, ControlFlowMode.End) => L.T("Ismétlés vége", "End Repeat"),
        (ControlFlowKind.Menu, ControlFlowMode.Start) => L.T("Választás menüből", "Choose from Menu"),
        (ControlFlowKind.Menu, ControlFlowMode.Middle) => L.T("Menüpont", "Menu Item"),
        (ControlFlowKind.Menu, ControlFlowMode.End) => L.T("Menü vége", "End Menu"),
        _ => Definition?.Name ?? ShortName(Action.Identifier),
    };

    public string Subtitle => Definition is null ? Action.Identifier : Definition.DisplayCategory;

    public string? Description => Definition?.DisplayDescription;

    public bool IsMacOnly => Definition?.MacOnly == true;

    /// <summary>The magic variable name of this action's output, if it has one.</summary>
    public string? OutputName => Action.Uuid is { } uuid
        ? Main.Scope.OutputNames.FirstOrDefault(n => Main.Scope.Resolve(n)?.OutputUuid == uuid)
        : null;

    public bool HasOutput => OutputName is not null;

    public string CategoryColor => CategoryStyle.ColorOf(Definition);

    public string Glyph => CategoryStyle.GlyphOf(Definition);

    // ------------------------------------------------------------------ collapsing

    /// <summary>Cards with editable content can be collapsed to a one-line summary.</summary>
    public bool CanCollapse => HasParams || IsIfStart || IsMenuCase;

    public bool IsCollapsed
    {
        get => Main.CollapsedActions.Contains(Action);
        set
        {
            if (value == IsCollapsed) return;
            if (value) Main.CollapsedActions.Add(Action);
            else Main.CollapsedActions.Remove(Action);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool IsExpanded => !IsCollapsed;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleCollapse() => IsCollapsed = !IsCollapsed;

    /// <summary>One-line preview of the set values, shown when collapsed.</summary>
    public string Summary
    {
        get
        {
            if (IsIfStart)
                return $"{ConditionInput} {ConditionOperator?.Label} {(ConditionNeedsValue ? ConditionValue : "")}".Trim();
            if (IsMenuCase) return CaseTitle;
            var parts = Params.Where(p => p.IsSet)
                .Select(p => p.Editor == EditorKind.Bool ? $"{p.Label}: {(p.Flag == true ? L.T("igen", "yes") : L.T("nem", "no"))}" : $"{p.Label}: {p.Text}")
                .Select(s => s.ReplaceLineEndings(" "));
            var text = string.Join("  ·  ", parts);
            return text.Length > 140 ? text[..140] + "…" : text;
        }
    }

    private static string ShortName(string identifier)
    {
        var last = identifier.Split('.').LastOrDefault() ?? identifier;
        return string.IsNullOrEmpty(last) ? identifier : last;
    }

    private void BuildParams()
    {
        if (IsMarker && !IsMenuCase) return;

        var keys = new List<(string Key, ParamDefinition? Definition)>();
        if (Definition is not null)
        {
            foreach (var p in Definition.Params)
            {
                if (IsIfStart && IfConditionKeys.Contains(p.Key)) continue;
                if (IsMenuStart && p.Key == "WFMenuItems") continue;
                if (IsMarker) continue;
                keys.Add((p.Key, p));
            }
        }
        foreach (var key in Action.Parameters.Keys)
        {
            if (HiddenKeys.Contains(key) || keys.Any(k => k.Key == key)) continue;
            if (IsIfStart && IfConditionKeys.Contains(key)) continue;
            if (IsMenuStart && key == "WFMenuItems") continue;
            if (IsMenuCase && key == "WFMenuItemTitle") continue;
            keys.Add((key, null));
        }
        foreach (var (key, def) in keys) Params.Add(new ParamViewModel(this, key, def));
    }

    /// <summary>Refreshes texts that depend on variable names.</summary>
    public void Refresh(ParamViewModel? except = null)
    {
        foreach (var p in Params)
            if (!ReferenceEquals(p, except) && p.Error is null)
                p.Load();
        if (except is null || !Params.Contains(except)) LoadSpecial();
        OnPropertyChanged(nameof(OutputName));
        OnPropertyChanged(nameof(HasOutput));
        OnPropertyChanged(nameof(Summary));
    }

    // ------------------------------------------------------------------ If condition

    public static IReadOnlyList<ConditionOption> ConditionOptions =>
        Conditions.All.Select(c => new ConditionOption(c.Code, c.Label)).ToList();

    [ObservableProperty] private string _conditionInput = "";
    [ObservableProperty] private ConditionOption? _conditionOperator;
    [ObservableProperty] private string _conditionValue = "";
    [ObservableProperty] private string _conditionValue2 = "";
    [ObservableProperty] private string? _conditionError;

    public bool ConditionNeedsValue => ConditionOperator is { } op && !Conditions.IsUnary(op.Code);
    public bool ConditionIsBetween => ConditionOperator?.Code == Conditions.IsBetween;

    // ------------------------------------------------------------------ Menu case

    [ObservableProperty] private string _caseTitle = "";

    private void LoadSpecial()
    {
        _loading = true;
        try
        {
            if (IsIfStart) LoadCondition();
            if (IsMenuCase) CaseTitle = (Action.Get("WFMenuItemTitle") as StringValue)?.Value ?? "";
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadCondition()
    {
        var scope = Main.Scope;
        ConditionInput = Action.Get("WFInput") is DictValue d && d["Variable"] is VariableValue v
            ? DslPrinter.PrintValue(v, ParamKind.Variable, scope)
            : "";
        var code = Action.Get("WFCondition") is IntegerValue i ? (int)i.Value : Conditions.HasAnyValue;
        ConditionOperator = ConditionOptions.FirstOrDefault(o => o.Code == code)
                            ?? new ConditionOption(code, L.T($"kód {code}", $"code {code}"));
        var value = Action.Get("WFConditionalActionString") ?? Action.Get("WFNumberValue");
        ConditionValue = value switch
        {
            null => "",
            TokenString t => DslPrinter.PrintTemplate(t, scope),
            _ => DslPrinter.PrintValue(value, ParamKind.Number, scope),
        };
        var another = Action.Get("WFAnotherNumber");
        ConditionValue2 = another is null ? "" : DslPrinter.PrintValue(another, ParamKind.Number, scope);
        ConditionError = null;
    }

    partial void OnConditionInputChanged(string value) => CommitCondition();
    partial void OnConditionValueChanged(string value) => CommitCondition();
    partial void OnConditionValue2Changed(string value) => CommitCondition();

    partial void OnConditionOperatorChanged(ConditionOption? value)
    {
        OnPropertyChanged(nameof(ConditionNeedsValue));
        OnPropertyChanged(nameof(ConditionIsBetween));
        CommitCondition();
    }

    private void CommitCondition()
    {
        if (_loading || !IsIfStart || ConditionOperator is null) return;
        var scope = Main.Scope;
        try
        {
            ParamValue? input = null;
            if (!string.IsNullOrWhiteSpace(ConditionInput))
            {
                var v = DslParser.ParseValue(ConditionInput, ParamKind.Variable, scope);
                if (v is not VariableValue variable) throw new DslException(L.T("A feltétel bal oldala változó kell legyen.", "The left side of the condition must be a variable."), default);
                input = new DictValue([new("Type", new StringValue("Variable")), new("Variable", variable)]);
            }

            var code = ConditionOperator.Code;
            ParamValue? text = null, number = null, number2 = null;
            if (!Conditions.IsUnary(code) && !string.IsNullOrWhiteSpace(ConditionValue))
            {
                if (Conditions.IsNumeric(code)) number = DslParser.ParseValue(ConditionValue, ParamKind.Number, scope);
                else text = DslParser.ParseTemplate(ConditionValue, scope);
            }
            if (code == Conditions.IsBetween && !string.IsNullOrWhiteSpace(ConditionValue2))
                number2 = DslParser.ParseValue(ConditionValue2, ParamKind.Number, scope);

            Main.Checkpoint();
            Action.SetOrRemove("WFInput", input);
            Action.Parameters["WFCondition"] = new IntegerValue(code);
            Action.SetOrRemove("WFConditionalActionString", text);
            Action.SetOrRemove("WFNumberValue", number);
            Action.SetOrRemove("WFAnotherNumber", number2);
            ConditionError = null;
            Main.OnParameterEdited(null);
        }
        catch (DslException ex)
        {
            ConditionError = ex.Message;
        }
    }

    partial void OnCaseTitleChanged(string value)
    {
        if (_loading || !IsMenuCase) return;
        Main.Checkpoint();
        Action.Parameters["WFMenuItemTitle"] = new StringValue(value);
        Main.SyncMenuItems(Action.GroupingIdentifier);
        Main.OnParameterEdited(null);
    }
}

public sealed record ConditionOption(int Code, string Label)
{
    public override string ToString() => Label;
}
