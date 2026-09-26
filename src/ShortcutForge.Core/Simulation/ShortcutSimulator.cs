using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;

namespace ShortcutForge.Core.Simulation;

/// <summary>What the simulator asks the user, standing in for the iPhone's dialogs.</summary>
public interface ISimulationHost
{
    /// <summary>Ask for Input. Returns null if the user cancelled (the shortcut stops, as on the iPhone).</summary>
    string? AskText(string prompt, string? defaultAnswer);

    /// <summary>Choose from Menu / Choose from List. Returns the chosen index, or null if cancelled.</summary>
    int? Choose(string prompt, IReadOnlyList<string> items);

    /// <summary>Show Alert. Returns false if the user pressed Cancel.</summary>
    bool Alert(string title, string message, bool cancelShown);
}

public enum SimStepKind
{
    /// <summary>Ran on Windows with the same result as on the iPhone.</summary>
    Ran,

    /// <summary>Has an effect only on the iPhone (notification, vibration, …); shown, not performed.</summary>
    Simulated,

    /// <summary>Cannot run on Windows; its output is a placeholder.</summary>
    Skipped,
}

/// <summary>One executed action of a dry run.</summary>
public sealed record SimStep(int Index, int Depth, string Action, SimStepKind Kind, string? Output, string? Note);

public sealed record SimulationResult(IReadOnlyList<SimStep> Steps, string? Output, string? StopReason);

/// <summary>An output the simulator cannot produce (e.g. a photo); shown by name.</summary>
public sealed record Placeholder(string Description)
{
    public override string ToString() => $"‹{Description}›";
}

/// <summary>
/// Dry-runs a shortcut on Windows: text, numbers, variables, lists, dictionaries, dates and
/// control flow run for real; device actions are shown or skipped. No files, network or
/// system settings are touched, and the real clipboard is never used.
/// </summary>
public sealed class ShortcutSimulator(ISimulationHost host, ActionCatalog? catalog = null)
{
    private const string Prefix = "is.workflow.actions.";
    public const int MaxSteps = 5000;

    private readonly ActionCatalog _catalog = catalog ?? ActionCatalog.Default;
    private readonly Dictionary<string, object?> _outputs = new();
    private readonly Dictionary<string, object?> _variables = new(StringComparer.Ordinal);
    private readonly List<SimStep> _steps = [];
    private IReadOnlyList<ActionInstance> _actions = [];
    private int[] _depth = [];
    private object? _previous;
    private object? _input;
    private string _clipboard = "";
    private CancellationToken _ct;

    private sealed class StopException(string? reason, object? output) : Exception(reason)
    {
        public object? Output { get; } = output;
    }

    public SimulationResult Run(Shortcut shortcut, string? shortcutInput = null, CancellationToken cancellationToken = default)
    {
        _actions = shortcut.Actions;
        _depth = ControlFlow.ComputeIndent(_actions);
        _outputs.Clear();
        _variables.Clear();
        _steps.Clear();
        _previous = null;
        _input = shortcutInput;
        _ct = cancellationToken;

        if (ControlFlow.Validate(_actions) is { } structureError)
            return new SimulationResult([], null, structureError);

        try
        {
            RunRange(0, _actions.Count);
            return new SimulationResult(_steps.ToList(), null, null);
        }
        catch (StopException stop)
        {
            return new SimulationResult(_steps.ToList(), stop.Output is null ? null : Text(stop.Output), stop.Message);
        }
    }

    // ---- Control flow -------------------------------------------------------------------

    private void RunRange(int from, int to)
    {
        var i = from;
        while (i < to)
        {
            var action = _actions[i];
            if (action.ControlFlow == ControlFlowMode.Start)
            {
                var (_, end) = ControlFlow.BlockRange(_actions, i);
                RunBlock(i, end);
                i = end + 1;
            }
            else
            {
                RunAction(i);
                i++;
            }
        }
    }

    /// <summary>Indices of the Middle actions (Otherwise / menu cases) directly belonging to the block.</summary>
    private List<int> Middles(int start, int end)
    {
        var group = _actions[start].GroupingIdentifier;
        var result = new List<int>();
        for (var i = start + 1; i < end; i++)
            if (_actions[i].GroupingIdentifier == group && _actions[i].ControlFlow == ControlFlowMode.Middle) result.Add(i);
        return result;
    }

    private void RunBlock(int start, int end)
    {
        var action = _actions[start];
        object? result = null;
        switch (Short(action.Identifier))
        {
            case "conditional":
            {
                var holds = EvaluateCondition(action, out var description);
                Step(start, SimStepKind.Ran, null, (holds ? L.T("igaz", "true") : L.T("hamis", "false")) + $" ({description})");
                var otherwise = Middles(start, end).FirstOrDefault(-1);
                if (holds) RunRange(start + 1, otherwise >= 0 ? otherwise : end);
                else if (otherwise >= 0) RunRange(otherwise + 1, end);
                result = _previous;
                break;
            }
            case "repeat.count":
            {
                var count = (int)Math.Max(0, Math.Round(Number(Param(action, "WFRepeatCount") ?? 1d)));
                Step(start, SimStepKind.Ran, null, L.T($"{count}× ismétlés", $"repeat {count}×"));
                var results = new List<object?>();
                for (var n = 1; n <= count; n++)
                {
                    _variables[VariableRef.RepeatIndex] = (double)n;
                    RunRange(start + 1, end);
                    results.Add(_previous);
                }
                result = results;
                break;
            }
            case "repeat.each":
            {
                var items = AsList(Param(action, "WFInput") ?? _previous);
                Step(start, SimStepKind.Ran, null, L.T($"{items.Count} elemen", $"over {items.Count} items"));
                var results = new List<object?>();
                for (var n = 0; n < items.Count; n++)
                {
                    _variables[VariableRef.RepeatIndex] = (double)(n + 1);
                    _variables[VariableRef.RepeatItem] = items[n];
                    RunRange(start + 1, end);
                    results.Add(_previous);
                }
                result = results;
                break;
            }
            case "choosefrommenu":
            {
                var cases = Middles(start, end);
                var titles = cases.Select(c => Text(Param(_actions[c], "WFMenuItemTitle") ?? "")).ToList();
                var prompt = Text(Param(action, "WFMenuPrompt") ?? "");
                var chosen = host.Choose(prompt, titles) ?? throw new StopException(L.T("A menüt bezártad, a parancs leállt.", "The menu was cancelled, so the shortcut stopped."), null);
                Step(start, SimStepKind.Ran, titles[chosen], null);
                RunRange(cases[chosen] + 1, chosen + 1 < cases.Count ? cases[chosen + 1] : end);
                result = _previous;
                break;
            }
            default:
                Step(start, SimStepKind.Skipped, null, L.T("Ismeretlen blokk, a tartalma kimarad.", "Unknown block; its contents are skipped."));
                break;
        }

        // The End action carries the block's output (If Result, Repeat Results, Menu Result).
        if (_actions[end].Uuid is { } uuid) _outputs[uuid] = result;
        _previous = result;
    }

    private bool EvaluateCondition(ActionInstance action, out string description)
    {
        var left = action.Get("WFInput") is DictValue d && d["Variable"] is { } v ? Eval(v) : Param(action, "WFInput") ?? _previous;
        var code = action.Get("WFCondition") is IntegerValue c ? (int)c.Value : Conditions.HasAnyValue;
        var label = Conditions.All.FirstOrDefault(x => x.Code == code).Label ?? code.ToString(CultureInfo.InvariantCulture);
        var leftText = Text(left);

        if (Conditions.IsUnary(code))
        {
            description = $"„{Shorten(leftText)}” {label}";
            var has = left is not null && !(left is string s && s.Length == 0) && !(left is List<object?> { Count: 0 });
            return code == Conditions.HasAnyValue ? has : !has;
        }

        if (Conditions.IsNumeric(code) || action.Get("WFNumberValue") is not null && action.Get("WFConditionalActionString") is null)
        {
            var a = Number(left);
            var b = Number(Param(action, "WFNumberValue") ?? 0d);
            description = $"{Format(a)} {label} {Format(b)}";
            return code switch
            {
                Conditions.LessThan => a < b,
                Conditions.LessOrEqual => a <= b,
                Conditions.GreaterThan => a > b,
                Conditions.GreaterOrEqual => a >= b,
                Conditions.IsBetween => a >= b && a <= Number(Param(action, "WFAnotherNumber") ?? 0d),
                Conditions.IsNot => a != b,
                _ => a == b,
            };
        }

        var right = Text(Param(action, "WFConditionalActionString") ?? "");
        description = $"„{Shorten(leftText)}” {label} „{Shorten(right)}”";
        var cmp = StringComparison.OrdinalIgnoreCase;
        return code switch
        {
            Conditions.Is => string.Equals(leftText, right, cmp),
            Conditions.IsNot => !string.Equals(leftText, right, cmp),
            Conditions.Contains => leftText.Contains(right, cmp),
            Conditions.DoesNotContain => !leftText.Contains(right, cmp),
            Conditions.BeginsWith => leftText.StartsWith(right, cmp),
            Conditions.EndsWith => leftText.EndsWith(right, cmp),
            _ => false,
        };
    }

    // ---- Plain actions ------------------------------------------------------------------

    private void RunAction(int index)
    {
        _ct.ThrowIfCancellationRequested();
        if (_steps.Count >= MaxSteps)
            throw new StopException(L.T($"Több mint {MaxSteps} lépés – leállítottam (végtelen ciklus?).", $"More than {MaxSteps} steps – stopped (endless loop?)."), null);

        var a = _actions[index];
        var kind = SimStepKind.Ran;
        string? note = null;
        object? output;

        object? P(string key) => Param(a, key);
        object? Input(string key = "WFInput") => a.Get(key) is null ? _previous : P(key);

        switch (Short(a.Identifier))
        {
            case "comment":
                return; // comments are not steps
            case "nothing":
                output = null;
                break;
            case "gettext":
                output = Text(P("WFTextActionText") ?? "");
                break;
            case "number":
                output = Number(P("WFNumberActionNumber") ?? 0d);
                break;
            case "url":
                output = Text(P("WFURLActionURL") ?? "");
                break;
            case "setvariable":
            {
                var name = Text(P("WFVariableName") ?? "");
                output = Input();
                _variables[name] = output;
                note = $"{name} = {Shorten(Text(output))}";
                break;
            }
            case "appendvariable":
            {
                var name = Text(P("WFVariableName") ?? "");
                var list = _variables.TryGetValue(name, out var existing) ? AsList(existing).ToList() : [];
                list.AddRange(AsList(Input()));
                _variables[name] = list;
                output = list;
                note = L.T($"{name}: {list.Count} elem", $"{name}: {list.Count} items");
                break;
            }
            case "getvariable":
                output = P("WFVariable");
                break;
            case "math":
                output = Calculate(a, Number(Input()));
                break;
            case "round":
                output = RoundNumber(a, Number(Input()));
                break;
            case "number.random":
            {
                var min = (int)Math.Round(Number(P("WFRandomNumberMinimum") ?? 0d));
                var max = (int)Math.Round(Number(P("WFRandomNumberMaximum") ?? 100d));
                output = (double)Random.Shared.Next(Math.Min(min, max), Math.Max(min, max) + 1);
                break;
            }
            case "list":
                output = AsList(P("WFItems"));
                break;
            case "count":
                output = Count(Input("Input"), Text(P("WFCountType") ?? "Items"));
                break;
            case "getitemfromlist":
                output = ItemFromList(a, AsList(Input()));
                break;
            case "choosefromlist":
            {
                var items = AsList(Input());
                var chosen = host.Choose(Text(P("WFChooseFromListActionPrompt") ?? ""), items.Select(Text).ToList())
                             ?? throw new StopException(L.T("A választást bezártad, a parancs leállt.", "The list was cancelled, so the shortcut stopped."), null);
                output = items[chosen];
                break;
            }
            case "dictionary":
                output = P("WFItems") as Dictionary<string, object?> ?? new Dictionary<string, object?>();
                break;
            case "getvalueforkey":
            {
                var dict = AsDictionary(Input());
                output = Text(P("WFGetDictionaryValueType") ?? "Value") switch
                {
                    "All Keys" => dict.Keys.Cast<object?>().ToList(),
                    "All Values" => dict.Values.ToList(),
                    _ => dict.GetValueOrDefault(Text(P("WFDictionaryKey") ?? "")),
                };
                break;
            }
            case "setvalueforkey":
            {
                var dict = new Dictionary<string, object?>(AsDictionary(Input("WFDictionary")))
                {
                    [Text(P("WFDictionaryKey") ?? "")] = P("WFDictionaryValue"),
                };
                output = dict;
                break;
            }
            case "text.replace":
                output = ReplaceText(a, Text(Input()));
                break;
            case "text.split":
                output = Split(Text(Input("text")), Text(P("WFTextSeparator") ?? "New Lines"), Text(P("WFTextCustomSeparator") ?? ""))
                    .Cast<object?>().ToList();
                break;
            case "text.combine":
            {
                var separator = Text(P("WFTextSeparator") ?? "New Lines") switch
                {
                    "Spaces" => " ",
                    "Custom" => Text(P("WFTextCustomSeparator") ?? ""),
                    _ => "\n",
                };
                output = string.Join(separator, AsList(Input("text")).Select(Text));
                break;
            }
            case "text.changecase":
                output = ChangeCase(Text(Input("text")), Text(P("WFCaseType") ?? "UPPERCASE"));
                break;
            case "date":
                output = Text(P("WFDateActionMode") ?? "Current Date") == "Specified Date" && P("WFDateActionDate") is { } spec
                    ? AsDate(spec) ?? (object)Text(spec)
                    : DateTime.Now;
                break;
            case "format.date":
                output = FormatDate(a, Input("WFDate"));
                break;
            case "getclipboard":
                output = _clipboard;
                note = L.T("szimulált vágólap", "simulated clipboard");
                break;
            case "setclipboard":
                _clipboard = Text(Input());
                output = null;
                kind = SimStepKind.Simulated;
                note = L.T("szimulált vágólapra másolva", "copied to the simulated clipboard");
                break;
            case "delay":
                output = null;
                kind = SimStepKind.Simulated;
                note = L.T($"{Format(Number(P("WFDelayTime") ?? 1d))} mp várakozás (kihagyva)", $"wait {Format(Number(P("WFDelayTime") ?? 1d))} s (skipped)");
                break;
            case "ask":
            {
                var prompt = Text(P("WFAskActionPrompt") ?? "");
                var answer = host.AskText(prompt, P("WFAskActionDefaultAnswer") is { } def ? Text(def) : null)
                             ?? throw new StopException(L.T("A kérdést bezártad, a parancs leállt.", "The question was cancelled, so the shortcut stopped."), null);
                output = Text(P("WFInputType") ?? "Text") == "Number" ? Number(answer) : answer;
                break;
            }
            case "alert":
            {
                var cancel = P("WFAlertActionCancelButtonShown") is not false;
                if (!host.Alert(Text(P("WFAlertActionTitle") ?? ""), Text(P("WFAlertActionMessage") ?? ""), cancel))
                {
                    Step(index, SimStepKind.Ran, null, L.T("Mégse", "Cancel"));
                    throw new StopException(L.T("A figyelmeztetésnél a Mégse gombot választottad, a parancs leállt.", "Cancel was pressed on the alert, so the shortcut stopped."), null);
                }
                output = null;
                break;
            }
            case "notification":
                output = null;
                kind = SimStepKind.Simulated;
                note = JoinNonEmpty(Text(P("WFNotificationActionTitle") ?? ""), Text(P("WFNotificationActionBody") ?? ""));
                break;
            case "showresult":
                output = null;
                kind = SimStepKind.Simulated;
                note = Text(P("Text") ?? _previous);
                break;
            case "speaktext":
                output = null;
                kind = SimStepKind.Simulated;
                note = Text(P("WFText") ?? _previous);
                break;
            case "previewdocument":
                output = null;
                kind = SimStepKind.Simulated;
                note = Text(Input());
                break;
            case "vibrate":
                output = null;
                kind = SimStepKind.Simulated;
                break;
            case "output":
            {
                var result = P("WFOutput");
                Step(index, SimStepKind.Ran, result is null ? null : Text(result), null);
                throw new StopException(L.T("A parancs kimenetet adott és véget ért.", "The shortcut produced its output and ended."), result);
            }
            case "exit":
            {
                var result = P("WFResult");
                Step(index, SimStepKind.Ran, result is null ? null : Text(result), null);
                throw new StopException(L.T("A parancs a Leállítás akciónál véget ért.", "The shortcut ended at the Stop action."), result);
            }
            default:
            {
                var definition = _catalog.ById(a.Identifier);
                output = definition?.Output is { } outputName ? new Placeholder(outputName) : null;
                kind = SimStepKind.Skipped;
                note = L.T("Windowson nem futtatható – az iPhone-on futna.", "Cannot run on Windows – it would run on the iPhone.");
                break;
            }
        }

        if (a.Uuid is { } uuid) _outputs[uuid] = output;
        _previous = output;
        Step(index, kind, output is null ? null : Text(output), note);
    }

    private void Step(int index, SimStepKind kind, string? output, string? note)
    {
        var action = _actions[index];
        var name = _catalog.ById(action.Identifier)?.Name ?? Short(action.Identifier);
        _steps.Add(new SimStep(index, _depth.Length > index ? _depth[index] : 0, name, kind, output is null ? null : Shorten(output, 400), note is null ? null : Shorten(note, 400)));
    }

    // ---- Values -------------------------------------------------------------------------

    private object? Param(ActionInstance action, string key) => action.Get(key) is { } v ? Eval(v) : null;

    private object? Eval(ParamValue value) => value switch
    {
        StringValue s => s.Value,
        IntegerValue i => (double)i.Value,
        RealValue r => r.Value,
        BoolValue b => b.Value,
        DateValue d => d.Value,
        TokenString t => EvalTokens(t),
        VariableValue v => Resolve(v.Variable),
        ArrayValue arr => arr.Items.Select(EvalListItem).ToList(),
        DictionaryFieldValue fields => fields.Items.ToDictionary(f => Text(EvalTokens(f.Key)), f => EvalField(f)),
        WrappedValue w => Eval(w.Value),
        DictValue d when d["WFValue"] is { } inner => Eval(inner),
        DictValue d when d["Value"] is { } inner => Eval(inner),
        _ => new Placeholder(L.T("érték", "value")),
    };

    private object? EvalListItem(ParamValue item) =>
        item is DictValue d && d["WFItemType"] is IntegerValue type && d["WFValue"] is { } value
            ? Typed((DictionaryItemType)type.Value, value)
            : Eval(item);

    private object? EvalField(DictionaryField field) => Typed(field.ItemType, field.Value);

    private object? Typed(DictionaryItemType type, ParamValue value)
    {
        var v = Eval(value);
        return type switch
        {
            DictionaryItemType.Number => double.TryParse(Text(v), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : v,
            DictionaryItemType.Boolean => v is bool ? v : Text(v) is "1" or "true" or "Yes",
            DictionaryItemType.Array => AsList(v),
            _ => v,
        };
    }

    private object? EvalTokens(TokenString t)
    {
        if (t.Attachments.Count == 1 && t.Text == TokenString.Placeholder.ToString())
            return Resolve(t.Attachments[0].Variable); // a lone variable keeps its type
        var sb = new StringBuilder();
        for (var i = 0; i < t.Text.Length; i++)
        {
            if (t.Text[i] == TokenString.Placeholder && t.Attachments.FirstOrDefault(x => x.Position == i) is { } att)
                sb.Append(Text(Resolve(att.Variable)));
            else
                sb.Append(t.Text[i]);
        }
        return sb.ToString();
    }

    private object? Resolve(VariableRef variable)
    {
        object? value = variable.Kind switch
        {
            VariableKinds.ActionOutput => variable.OutputUuid is { } id && _outputs.TryGetValue(id, out var o) ? o : null,
            VariableKinds.Variable => variable.VariableName is { } name && _variables.TryGetValue(name, out var v) ? v : null,
            VariableKinds.ShortcutInput => _input,
            VariableKinds.CurrentDate => DateTime.Now,
            VariableKinds.Clipboard => _clipboard,
            VariableKinds.Ask => host.AskText(L.T("Kérdezés futáskor", "Ask Each Time"), null)
                                 ?? throw new StopException(L.T("A kérdést bezártad, a parancs leállt.", "The question was cancelled, so the shortcut stopped."), null),
            VariableKinds.DeviceDetails => new Placeholder(L.T("eszköz adatai", "device details")),
            _ => new Placeholder(variable.Kind),
        };

        foreach (var item in Aggrandizements.Of(variable))
        {
            if (item is not DictValue d) continue;
            switch ((d["Type"] as StringValue)?.Value)
            {
                case Aggrandizements.DictionaryValueType when d["DictionaryKey"] is StringValue key:
                    value = AsDictionary(value).GetValueOrDefault(key.Value);
                    break;
                case Aggrandizements.CoercionType when d["CoercionItemClass"] is StringValue cls:
                    value = cls.Value switch
                    {
                        "WFNumberContentItem" => Number(value),
                        "WFStringContentItem" => Text(value),
                        "WFBooleanContentItem" => value is bool ? value : Number(value) != 0,
                        "WFDictionaryContentItem" => AsDictionary(value),
                        "WFDateContentItem" => AsDate(value) ?? value,
                        _ => value,
                    };
                    break;
                case Aggrandizements.PropertyType when d["PropertyName"] is StringValue prop:
                    value = prop.Value == "Name" ? Text(value) : new Placeholder(prop.Value);
                    break;
            }
        }
        return value;
    }

    public static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        double d => Format(d),
        bool b => b ? L.T("Igen", "Yes") : L.T("Nem", "No"),
        DateTime dt => dt.ToString("g", L.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("hu-HU")),
        List<object?> list => string.Join("\n", list.Select(Text)),
        Dictionary<string, object?> dict => JsonSerializer.Serialize(ToJson(dict), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
        _ => value.ToString() ?? "",
    };

    private static object? ToJson(object? value) => value switch
    {
        Dictionary<string, object?> d => d.ToDictionary(e => e.Key, e => ToJson(e.Value)),
        List<object?> l => l.Select(ToJson).ToList(),
        Placeholder p => p.ToString(),
        DateTime dt => dt.ToString("s", CultureInfo.InvariantCulture),
        _ => value,
    };

    public static string Format(double d) =>
        double.IsFinite(d) && Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15
            ? Math.Round(d).ToString("0", CultureInfo.InvariantCulture)
            : d.ToString("0.##########", CultureInfo.InvariantCulture);

    private static double Number(object? value) => value switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        List<object?> { Count: > 0 } l => Number(l[0]),
        _ => double.TryParse(Text(value).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0,
    };

    private static List<object?> AsList(object? value) => value switch
    {
        null => [],
        List<object?> l => l,
        _ => [value],
    };

    private static Dictionary<string, object?> AsDictionary(object? value)
    {
        switch (value)
        {
            case Dictionary<string, object?> d:
                return d;
            case string s when s.TrimStart().StartsWith('{'):
                try
                {
                    return FromJson(JsonDocument.Parse(s).RootElement) as Dictionary<string, object?> ?? [];
                }
                catch (JsonException)
                {
                    return [];
                }
            default:
                return [];
        }
    }

    private static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        _ => null,
    };

    private static DateTime? AsDate(object? value) => value switch
    {
        DateTime d => d,
        _ => DateTime.TryParse(Text(value), CultureInfo.CurrentCulture, DateTimeStyles.None, out var d) ? d
           : DateTime.TryParse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : null,
    };

    // ---- Action helpers -----------------------------------------------------------------

    private double Calculate(ActionInstance a, double x)
    {
        var op = Text(Param(a, "WFMathOperation") ?? "+");
        var y = Number(Param(a, "WFMathOperand") ?? 0d);
        return op switch
        {
            "+" => x + y,
            "-" => x - y,
            "×" or "*" or "x" => x * y,
            "÷" or "/" => x / y,
            _ => Text(Param(a, "WFScientificMathOperation") ?? "") switch
            {
                "Modulus" => x % Number(Param(a, "WFScientificMathOperand") ?? 1d),
                "x^2" => x * x,
                "x^3" => x * x * x,
                "x^y" => Math.Pow(x, Number(Param(a, "WFScientificMathOperand") ?? 1d)),
                "e^x" => Math.Exp(x),
                "10^x" => Math.Pow(10, x),
                "ln(x)" => Math.Log(x),
                "log(x)" => Math.Log10(x),
                "√x" => Math.Sqrt(x),
                "∛x" => Math.Cbrt(x),
                "x!" => Enumerable.Range(1, Math.Max(0, (int)x)).Aggregate(1d, (p, k) => p * k),
                "ABS(x)" => Math.Abs(x),
                _ => x,
            },
        };
    }

    private double RoundNumber(ActionInstance a, double x)
    {
        var places = Text(Param(a, "WFRoundTo") ?? "Ones Place") switch
        {
            "Tenths" => 1, "Hundredths" => 2, "Thousandths" => 3, "Ten Thousandths" => 4,
            "Hundred Thousandths" => 5, "Millionths" => 6, "Tens" => -1, "Hundreds" => -2, "Thousands" => -3,
            _ => 0,
        };
        var scale = Math.Pow(10, places);
        return Text(Param(a, "WFRoundMode") ?? "Normal") switch
        {
            "Always Round Up" => Math.Ceiling(x * scale) / scale,
            "Always Round Down" => Math.Floor(x * scale) / scale,
            _ => Math.Round(x * scale, MidpointRounding.AwayFromZero) / scale,
        };
    }

    private static double Count(object? input, string type)
    {
        var text = Text(input);
        return type switch
        {
            "Characters" => text.Length,
            "Words" => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            "Sentences" => Regex.Matches(text, @"[^.!?]+[.!?]*").Count(m => m.Value.Trim().Length > 0),
            "Lines" => text.Length == 0 ? 0 : text.Split('\n').Length,
            _ => AsList(input).Count,
        };
    }

    private object? ItemFromList(ActionInstance a, List<object?> items)
    {
        if (items.Count == 0) return null;
        int Index(string key, double fallback) => (int)Math.Round(Number(Param(a, key) ?? fallback));
        return Text(Param(a, "WFItemSpecifier") ?? "First Item") switch
        {
            "Last Item" => items[^1],
            "Random Item" => items[Random.Shared.Next(items.Count)],
            "Item At Index" => Index("WFItemIndex", 1) is var i && i >= 1 && i <= items.Count ? items[i - 1] : null,
            "Items in Range" => items.Skip(Math.Max(0, Index("WFItemRangeStart", 1) - 1))
                .Take(Math.Max(0, Index("WFItemRangeEnd", items.Count) - Math.Max(0, Index("WFItemRangeStart", 1) - 1))).ToList(),
            _ => items[0],
        };
    }

    private string ReplaceText(ActionInstance a, string input)
    {
        var find = Text(Param(a, "WFReplaceTextFind") ?? "");
        var replace = Text(Param(a, "WFReplaceTextReplace") ?? "");
        var caseSensitive = Param(a, "WFReplaceTextCaseSensitive") is true;
        if (find.Length == 0) return input;
        if (Param(a, "WFReplaceTextRegularExpression") is true)
        {
            try
            {
                return Regex.Replace(input, find, replace, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return input;
            }
        }
        return input.Replace(find, replace, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Split(string text, string separator, string custom) => separator switch
    {
        "Spaces" => text.Split(' ', StringSplitOptions.RemoveEmptyEntries),
        "Every Character" => text.Select(c => c.ToString()).ToArray(),
        "Custom" => custom.Length == 0 ? [text] : text.Split(custom),
        _ => text.Replace("\r\n", "\n").Split('\n'),
    };

    private static string ChangeCase(string text, string mode)
    {
        var culture = CultureInfo.CurrentCulture;
        return mode switch
        {
            "lowercase" => text.ToLower(culture),
            "Capitalize Every Word" or "Capitalize with Title Case" => culture.TextInfo.ToTitleCase(text.ToLower(culture)),
            "Capitalize with sentence case." => text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..].ToLower(culture),
            "cApItAlIzE wItH aLtErNaTiNg cAsE." => string.Concat(text.Select((c, i) => i % 2 == 0 ? char.ToLower(c, culture) : char.ToUpper(c, culture))),
            _ => text.ToUpper(culture),
        };
    }

    private string FormatDate(ActionInstance a, object? input)
    {
        if (AsDate(input) is not { } date) return Text(input);
        var culture = L.IsEnglish ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("hu-HU");
        var dateStyle = Text(Param(a, "WFDateFormatStyle") ?? "Short");
        if (dateStyle == "ISO 8601") return date.ToString(Param(a, "WFISO8601IncludeTime") is true ? "yyyy-MM-dd'T'HH:mm:ss" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (dateStyle == "RFC 2822") return date.ToString("r", CultureInfo.InvariantCulture);
        if (dateStyle == "Custom")
        {
            try
            {
                return date.ToString(Text(Param(a, "WFDateFormat") ?? "yyyy-MM-dd").Replace("a", "tt"), culture);
            }
            catch (FormatException)
            {
                return Text(date);
            }
        }
        var datePart = dateStyle switch { "None" => "", "Medium" => date.ToString("m", culture) + " " + date.Year, "Long" => date.ToString("D", culture), _ => date.ToString("d", culture) };
        var timePart = Text(Param(a, "WFTimeFormatStyle") ?? "Short") switch { "None" => "", "Medium" or "Long" => date.ToString("T", culture), _ => date.ToString("t", culture) };
        return JoinNonEmpty(datePart, timePart, " ");
    }

    private static string JoinNonEmpty(string a, string b, string separator = " – ") =>
        a.Length == 0 ? b : b.Length == 0 ? a : a + separator + b;

    private static string Short(string identifier) => identifier.StartsWith(Prefix, StringComparison.Ordinal) ? identifier[Prefix.Length..] : identifier;

    private static string Shorten(string text, int max = 60) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
