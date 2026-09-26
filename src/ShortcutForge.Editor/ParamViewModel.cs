using CommunityToolkit.Mvvm.ComponentModel;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;
using ShortcutForge.Dsl;

namespace ShortcutForge.Editor;

public enum EditorKind
{
    /// <summary>Text with {variables}, no quotes.</summary>
    Template,
    /// <summary>DSL value expression, e.g. 42, "text", variable, { "k": "v" }.</summary>
    Expression,
    /// <summary>A single variable, with suggestions.</summary>
    Variable,
    Bool,
    Enum,
    /// <summary>Plain text without variables, taken literally.</summary>
    Plain,
}

/// <summary>One editable parameter row on an action card.</summary>
public sealed partial class ParamViewModel : ObservableObject
{
    private readonly ActionCardViewModel _owner;
    private bool _loading;

    public ParamViewModel(ActionCardViewModel owner, string key, ParamDefinition? definition)
    {
        _owner = owner;
        Key = key;
        Definition = definition;
        Kind = definition?.Type ?? ParamKind.Raw;
    }

    public string Key { get; }
    public ParamDefinition? Definition { get; }
    public ParamKind Kind { get; }

    public string Label => Definition?.DisplayLabel ?? Key;

    public string ToolTip => Definition is null
        ? L.T($"{Key} (a katalógusban nem szereplő paraméter, nyers érték)", $"{Key} (parameter not in the catalog, raw value)")
        : L.T($"{Definition.Key} · DSL név: {Definition.Name} · típus: {Kind}", $"{Definition.Key} · DSL name: {Definition.Name} · type: {Kind}") +
          (Definition.Description is { } d ? "\n" + d : "");

    public bool IsMultiline => Kind is ParamKind.Dictionary or ParamKind.List or ParamKind.Raw
                               || Editor is EditorKind.Template or EditorKind.Plain;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextEditor), nameof(IsVariableEditor), nameof(IsEnumEditor), nameof(IsBoolEditor),
        nameof(IsMonospace), nameof(IsMultiline))]
    private EditorKind _editor;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool? _flag;
    [ObservableProperty] private string? _error;

    // Which editor control the view shows (UI frameworks without data triggers bind to these).
    public bool IsTextEditor => Editor is EditorKind.Template or EditorKind.Plain or EditorKind.Expression;
    public bool IsVariableEditor => Editor == EditorKind.Variable;
    public bool IsEnumEditor => Editor == EditorKind.Enum;
    public bool IsBoolEditor => Editor == EditorKind.Bool;
    public bool IsMonospace => Editor is EditorKind.Expression or EditorKind.Variable;

    public IReadOnlyList<string> Options => Definition?.Options ?? [];

    public IReadOnlyList<string> VariableSuggestions => _owner.Main.VariableSuggestions;

    public bool IsSet => _owner.Action.Parameters.ContainsKey(Key);

    public string Placeholder => Editor switch
    {
        EditorKind.Template => L.T("szöveg, változó: {név}", "text, variable: {name}"),
        EditorKind.Plain => L.T("szöveg", "text"),
        EditorKind.Variable => L.T("változó neve", "variable name"),
        EditorKind.Enum => L.T("(alapértelmezett)", "(default)"),
        _ => Kind switch
        {
            ParamKind.Number or ParamKind.Integer => L.T("szám vagy változó", "number or variable"),
            ParamKind.Dictionary => L.T("{ \"kulcs\": \"érték\" }", "{ \"key\": \"value\" }"),
            ParamKind.List => L.T("[\"egy\", \"kettő\"]", "[\"one\", \"two\"]"),
            _ => L.T("érték", "value"),
        },
    };

    /// <summary>Reloads the displayed text from the model (after names may have changed).</summary>
    public void Load()
    {
        _loading = true;
        try
        {
            var value = _owner.Action.Get(Key);
            var scope = _owner.Main.Scope;
            Error = null;
            switch (Kind)
            {
                case ParamKind.Bool when value is null or BoolValue:
                    Editor = EditorKind.Bool;
                    Flag = (value as BoolValue)?.Value;
                    break;
                case ParamKind.Enum when value is null or StringValue:
                    Editor = EditorKind.Enum;
                    Text = (value as StringValue)?.Value ?? "";
                    break;
                case ParamKind.String when value is null or StringValue:
                    Editor = EditorKind.Plain;
                    Text = (value as StringValue)?.Value ?? "";
                    break;
                case ParamKind.Text when value is null or TokenString:
                    Editor = EditorKind.Template;
                    Text = value is TokenString t ? DslPrinter.PrintTemplate(t, scope) : "";
                    break;
                case ParamKind.Variable when value is null or VariableValue:
                    Editor = EditorKind.Variable;
                    Text = value is null ? "" : DslPrinter.PrintValue(value, Kind, scope);
                    break;
                default:
                    Editor = EditorKind.Expression;
                    Text = value is null ? "" : DslPrinter.PrintValue(value, Kind, scope);
                    break;
            }
            OnPropertyChanged(nameof(Placeholder));
            OnPropertyChanged(nameof(IsSet));
            OnPropertyChanged(nameof(VariableSuggestions));
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnTextChanged(string value)
    {
        if (!_loading) Commit();
    }

    /// <summary>Bool editor choices: default (not set) / yes / no.</summary>
    public IReadOnlyList<string> BoolOptions => [L.T("(alapértelmezett)", "(default)"), L.T("Igen", "Yes"), L.T("Nem", "No")];

    public int BoolChoice
    {
        get => Flag switch { null => 0, true => 1, false => 2 };
        set => Flag = value switch { 1 => true, 2 => false, _ => null };
    }

    partial void OnFlagChanged(bool? value)
    {
        OnPropertyChanged(nameof(BoolChoice));
        if (_loading) return;
        _owner.Main.Checkpoint();
        _owner.Action.SetOrRemove(Key, value is null ? null : new BoolValue(value.Value));
        _owner.Main.OnParameterEdited(this);
    }

    private void Commit()
    {
        ParamValue? value;
        try
        {
            value = Parse(Text ?? "");
            Error = null;
        }
        catch (DslException ex)
        {
            Error = ex.Message;
            return;
        }

        if (Equals(value, _owner.Action.Get(Key))) return;
        _owner.Main.Checkpoint();
        _owner.Action.SetOrRemove(Key, value);
        _owner.Main.OnParameterEdited(this);
        OnPropertyChanged(nameof(IsSet));
    }

    private ParamValue? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) && Editor is not (EditorKind.Template or EditorKind.Plain)) return null;
        if (text.Length == 0) return null;
        var scope = _owner.Main.Scope;
        return Editor switch
        {
            EditorKind.Template => DslParser.ParseTemplate(text, scope),
            EditorKind.Enum or EditorKind.Plain => new StringValue(text),
            _ => DslParser.ParseValue(text, Kind, scope),
        };
    }
}
