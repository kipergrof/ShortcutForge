using System.Globalization;
using System.Text;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;

namespace ShortcutForge.Dsl;

/// <summary>
/// Turns a <see cref="Shortcut"/> into DSL text. Parsing the output again yields an equivalent
/// shortcut: every value that cannot be expressed in the friendly syntax falls back to a raw form.
/// </summary>
public sealed class DslPrinter
{
    private static readonly HashSet<string> StructuralKeys =
        [ActionInstance.UuidKey, ActionInstance.GroupingKey, ActionInstance.ControlFlowKey];

    private readonly ActionCatalog _catalog;
    private readonly NameScope _scope = new();
    private readonly StringBuilder _out = new();
    private readonly Dictionary<string, string> _namesByUuid = new();
    private readonly Dictionary<string, string> _outputNameByUuid = new();
    private readonly HashSet<string> _referencedUuids = new();
    private int _indent;

    private DslPrinter(ActionCatalog catalog) => _catalog = catalog;

    public static string Print(Shortcut shortcut, ActionCatalog? catalog = null)
    {
        var printer = new DslPrinter(catalog ?? ActionCatalog.Default);
        printer.PrintShortcut(shortcut);
        return printer._out.ToString();
    }

    /// <summary>
    /// Computes the identifiers used for magic variables. The visual editor uses the same
    /// names, so both views show the same thing.
    /// </summary>
    public static NameScope BuildScope(IReadOnlyList<ActionInstance> actions, ActionCatalog? catalog = null)
    {
        var printer = new DslPrinter(catalog ?? ActionCatalog.Default);
        printer.AssignNames(actions);
        return printer._scope;
    }

    /// <summary>Prints a single value as a DSL expression (visual editor fields).</summary>
    public static string PrintValue(ParamValue value, ParamKind kind, NameScope scope)
    {
        var printer = new DslPrinter(ActionCatalog.Default);
        foreach (var name in scope.NamedVariables) printer._scope.DeclareVariable(name);
        foreach (var name in scope.OutputNames) printer._scope.BindOutput(name, scope.Resolve(name)!);
        return printer.Value(value, kind);
    }

    /// <summary>Text field content without the surrounding quotes, e.g. <c>Hello {name}</c>.</summary>
    public static string PrintTemplate(TokenString value, NameScope scope)
    {
        var printer = new DslPrinter(ActionCatalog.Default);
        foreach (var name in scope.NamedVariables) printer._scope.DeclareVariable(name);
        foreach (var name in scope.OutputNames) printer._scope.BindOutput(name, scope.Resolve(name)!);
        var quoted = printer.Template(value);
        return UnescapeTemplate(quoted[1..^1]);
    }

    /// <summary>Undoes string-literal escaping except for braces, which stay escaped in templates.</summary>
    private static string UnescapeTemplate(string inner)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                var next = inner[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; continue;
                    case 't': sb.Append('\t'); i++; continue;
                    case 'r': sb.Append('\r'); i++; continue;
                    case '"': sb.Append('"'); i++; continue;
                    case '\\': sb.Append('\\'); i++; continue;
                    case 'u' when i + 6 <= inner.Length:
                        sb.Append((char)Convert.ToInt32(inner.Substring(i + 2, 4), 16));
                        i += 5;
                        continue;
                }
            }
            sb.Append(inner[i]);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ names

    private void AssignNames(IReadOnlyList<ActionInstance> actions)
    {
        foreach (var a in actions)
            if (a.Identifier is "is.workflow.actions.setvariable" or "is.workflow.actions.appendvariable" &&
                a.Get("WFVariableName") is StringValue vn && NameScope.IsIdentifier(vn.Value))
                _scope.DeclareVariable(vn.Value);

        // Which action outputs are referenced, and under which OutputName?
        var referenced = new Dictionary<string, string>();
        foreach (var a in actions)
            foreach (var v in a.Parameters.Values.SelectMany(EnumerateVariables))
                if (v.Kind == VariableKinds.ActionOutput && v.OutputUuid is { } uuid)
                    referenced.TryAdd(uuid, v.OutputName ?? "Output");
        _referencedUuids.UnionWith(referenced.Keys);

        // Every action with an output gets a name (not only referenced ones) so names stay stable
        // while editing and the visual editor can offer them; only referenced ones are printed.
        var used = new HashSet<string>(_scope.NamedVariables, StringComparer.Ordinal);
        foreach (var a in actions)
        {
            if (a.Uuid is not { } uuid || _namesByUuid.ContainsKey(uuid)) continue;
            if (!referenced.TryGetValue(uuid, out var outputName))
            {
                var definition = _catalog.ById(a.Identifier);
                if (definition?.Output is null || (definition.Control != ControlFlowKind.None && a.ControlFlow != ControlFlowMode.End))
                    continue;
                outputName = a.CustomOutputName ?? definition.Output;
            }
            var baseName = ToIdentifier(a.CustomOutputName ?? outputName);
            var name = baseName;
            for (var n = 2; used.Contains(name) || !NameScope.IsIdentifier(name); n++) name = baseName + n;
            used.Add(name);
            _namesByUuid[uuid] = name;
            _outputNameByUuid[uuid] = outputName;
            _scope.BindOutput(name, VariableRef.Output(uuid, outputName));
        }
    }

    private static string ToIdentifier(string text)
    {
        var sb = new StringBuilder();
        var upper = false;
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            if (Lexer.IsIdentPart(c))
            {
                sb.Append(sb.Length == 0 ? char.ToLowerInvariant(c) : upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else upper = sb.Length > 0;
        }
        if (sb.Length == 0 || !Lexer.IsIdentStart(sb[0])) sb.Insert(0, "v");
        return sb.ToString();
    }

    public static IEnumerable<VariableRef> EnumerateVariables(ParamValue value)
    {
        switch (value)
        {
            case VariableValue v:
                yield return v.Variable;
                break;
            case TokenString t:
                foreach (var a in t.Attachments) yield return a.Variable;
                break;
            case ArrayValue arr:
                foreach (var v in arr.Items.SelectMany(EnumerateVariables)) yield return v;
                break;
            case DictValue d:
                foreach (var v in d.Entries.SelectMany(e => EnumerateVariables(e.Value))) yield return v;
                break;
            case DictionaryFieldValue f:
                foreach (var item in f.Items)
                {
                    foreach (var v in EnumerateVariables(item.Key)) yield return v;
                    foreach (var v in EnumerateVariables(item.Value)) yield return v;
                }
                break;
            case WrappedValue w:
                foreach (var v in EnumerateVariables(w.Value)) yield return v;
                break;
        }
    }

    // ------------------------------------------------------------------ program

    private void PrintShortcut(Shortcut shortcut)
    {
        AssignNames(shortcut.Actions);

        Line($"#name {Str(shortcut.Name)}");
        var colorName = ShortcutIcon.NameOfColor(shortcut.Icon.StartColor);
        Line($"#icon color={colorName ?? shortcut.Icon.StartColor.ToString(CultureInfo.InvariantCulture)} glyph={shortcut.Icon.GlyphNumber}");
        if (shortcut.InputClasses.Count > 0) Line("#input " + string.Join(", ", shortcut.InputClasses.Select(NameOrString)));
        if (shortcut.Types.Count > 0) Line("#type " + string.Join(", ", shortcut.Types.Select(NameOrString)));
        if (shortcut.ClientVersion != Shortcut.DefaultClientVersion) Line($"#version {Str(shortcut.ClientVersion)}");
        foreach (var (key, value) in shortcut.Extra)
            if (!IsDefaultExtra(key, value))
                Line($"#meta {NameOrString(key)} = {Raw(value)}");
        _out.AppendLine();

        if (ControlFlow.Validate(shortcut.Actions) is null)
            PrintRange(shortcut.Actions, 0, shortcut.Actions.Count);
        else
            foreach (var a in shortcut.Actions) PrintGeneric(a, includeStructure: true);
    }

    private static bool IsDefaultExtra(string key, ParamValue value) => (key, value) switch
    {
        ("WFWorkflowImportQuestions", ArrayValue { Items.Count: 0 }) => true,
        ("WFWorkflowMinimumClientVersion", IntegerValue { Value: 900 }) => true,
        ("WFWorkflowMinimumClientVersionString", StringValue { Value: "900" }) => true,
        _ => false,
    };

    private static string NameOrString(string s) =>
        s.Length > 0 && Lexer.IsIdentStart(s[0]) && s.All(Lexer.IsIdentPart) ? s : Str(s);

    private void Line(string text)
    {
        _out.Append(' ', _indent * 4).Append(text).Append('\n');
    }

    /// <summary>Prints actions [from, to), recursing into blocks.</summary>
    private void PrintRange(IReadOnlyList<ActionInstance> actions, int from, int to)
    {
        var i = from;
        while (i < to)
        {
            var a = actions[i];
            if (a.ControlFlow == ControlFlowMode.Start)
            {
                var (_, last) = ControlFlow.BlockRange(actions, i);
                PrintBlock(actions, i, last);
                i = last + 1;
            }
            else
            {
                PrintAction(a);
                i++;
            }
        }
    }

    private void PrintBlock(IReadOnlyList<ActionInstance> actions, int first, int last)
    {
        var start = actions[first];
        var end = actions[last];
        var group = start.GroupingIdentifier;

        // Direct middle actions of this block (not those of nested blocks with other groups).
        var middles = new List<int>();
        for (var i = first + 1; i < last; i++)
            if (actions[i].GroupingIdentifier == group && actions[i].ControlFlow == ControlFlowMode.Middle)
                middles.Add(i);

        var id = start.Identifier;
        var knownStructure = id switch
        {
            ControlFlow.IfId => middles.Count <= 1,
            ControlFlow.RepeatId or ControlFlow.RepeatEachId => middles.Count == 0,
            ControlFlow.MenuId => true,
            _ => false,
        };
        if (!knownStructure || actions.Skip(first).Take(last - first + 1)
                .Any(a => a.GroupingIdentifier == group && a.Identifier != id))
        {
            // Unknown block type: print flat, keeping the structure keys.
            for (var i = first; i <= last; i++) PrintGeneric(actions[i], includeStructure: true);
            return;
        }

        var definition = _catalog.ById(id);
        var suffix = EndSuffix(end);

        switch (id)
        {
            case ControlFlow.IfId:
            {
                var (condition, rest) = Condition(start);
                Line($"if {condition}{With(rest, definition)} {{");
                var bodyEnd = middles.Count == 1 ? middles[0] : last;
                Indented(() => PrintRange(actions, first + 1, bodyEnd));
                if (middles.Count == 1)
                {
                    var middle = actions[middles[0]];
                    Line($"}} else{With(Leftover(middle), definition)} {{");
                    Indented(() => PrintRange(actions, middles[0] + 1, last));
                }
                Line("}" + suffix);
                break;
            }
            case ControlFlow.RepeatId:
            case ControlFlow.RepeatEachId:
            {
                var rest = Leftover(start);
                var (paramKey, keyword, kind) = id == ControlFlow.RepeatId
                    ? ("WFRepeatCount", "repeat", ParamKind.Number)
                    : ("WFInput", "foreach", ParamKind.Variable);
                var head = keyword;
                if (rest.Remove(paramKey, out var arg)) head += " " + Value(arg, kind);
                Line($"{head}{With(rest, definition)} {{");
                Indented(() => PrintRange(actions, first + 1, last));
                Line("}" + suffix);
                break;
            }
            case ControlFlow.MenuId:
            {
                var rest = Leftover(start);
                var head = "menu";
                if (rest.Remove("WFMenuPrompt", out var prompt)) head += " " + Value(prompt, ParamKind.Text);

                // WFMenuItems is rebuilt from the case titles; keep it only if it differs.
                var titles = middles.Select(m => actions[m].Get("WFMenuItemTitle")).ToList();
                if (rest.TryGetValue("WFMenuItems", out var items) &&
                    titles.All(t => t is StringValue) &&
                    items.Equals(new ArrayValue(titles.Cast<ParamValue>().ToList())))
                    rest.Remove("WFMenuItems");

                Line($"{head}{With(rest, definition)} {{");
                Indented(() =>
                {
                    for (var k = 0; k < middles.Count; k++)
                    {
                        var middle = actions[middles[k]];
                        var caseRest = Leftover(middle);
                        caseRest.Remove("WFMenuItemTitle", out var title);
                        var titleText = title is StringValue ? Value(title, ParamKind.String) : Str("");
                        if (title is not null and not StringValue) caseRest["WFMenuItemTitle"] = title;
                        Line($"case {titleText}{With(caseRest, definition)} {{");
                        var bodyEnd = k + 1 < middles.Count ? middles[k + 1] : last;
                        Indented(() => PrintRange(actions, middles[k] + 1, bodyEnd));
                        Line("}");
                    }
                });
                Line("}" + suffix);
                break;
            }
        }
    }

    private bool IsBound(ActionInstance a, out string uuid, out string name)
    {
        uuid = a.Uuid ?? "";
        name = "";
        return a.Uuid is not null && _referencedUuids.Contains(uuid) && _namesByUuid.TryGetValue(uuid, out name!);
    }

    private string EndSuffix(ActionInstance end) =>
        IsBound(end, out var uuid, out var name) ? $" -> {name}{AsClause(uuid, end)}" : "";

    private string AsClause(string uuid, ActionInstance action)
    {
        var outputName = _outputNameByUuid[uuid];
        var defaultName = action.CustomOutputName ?? _catalog.ById(action.Identifier)?.Output ?? "Output";
        return outputName == defaultName ? "" : $" as {Str(outputName)}";
    }

    private void Indented(Action body)
    {
        _indent++;
        body();
        _indent--;
    }

    private static Dictionary<string, ParamValue> Leftover(ActionInstance a) =>
        a.Parameters.Where(p => !StructuralKeys.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);

    private string With(Dictionary<string, ParamValue> rest, ActionDefinition? definition) =>
        rest.Count == 0 ? "" : " with (" + string.Join(", ", rest.Select(p => NamedArg(p.Key, p.Value, definition))) + ")";

    private (string Text, Dictionary<string, ParamValue> Leftover) Condition(ActionInstance start)
    {
        var rest = Leftover(start);
        var sb = new StringBuilder();

        // Left side
        if (rest.TryGetValue("WFInput", out var input) && input is DictValue d &&
            d.Entries.Count == 2 && d["Type"] is StringValue { Value: "Variable" } && d["Variable"] is VariableValue v)
        {
            sb.Append(VariableExpr(v.Variable));
            rest.Remove("WFInput");
        }
        else sb.Append('_');

        rest.Remove("WFCondition", out var codeValue);
        long code = Conditions.HasAnyValue;
        if (codeValue is IntegerValue { Value: var c }) code = c;
        else if (codeValue is not null) rest["WFCondition"] = codeValue; // unusual type: overridden via with()

        var op = code switch
        {
            Conditions.Is => "==",
            Conditions.IsNot => "!=",
            Conditions.LessThan => "<",
            Conditions.LessOrEqual => "<=",
            Conditions.GreaterThan => ">",
            Conditions.GreaterOrEqual => ">=",
            Conditions.Contains => "contains",
            Conditions.DoesNotContain => "!contains",
            Conditions.BeginsWith => "beginsWith",
            Conditions.EndsWith => "endsWith",
            Conditions.HasAnyValue => "hasValue",
            Conditions.DoesNotHaveAnyValue => "!hasValue",
            Conditions.IsBetween => "between",
            _ => "?" + code.ToString(CultureInfo.InvariantCulture),
        };
        sb.Append(' ').Append(op);

        if (Conditions.IsUnary((int)code)) return (sb.ToString(), rest);

        // Right side: the parser puts number literals and numeric operators into WFNumberValue,
        // everything else into WFConditionalActionString. Print only what maps back exactly.
        if (code == Conditions.IsBetween)
        {
            if (rest.TryGetValue("WFNumberValue", out var a) && rest.TryGetValue("WFAnotherNumber", out var b))
            {
                sb.Append(' ').Append(Value(a, ParamKind.Number)).Append(" and ").Append(Value(b, ParamKind.Number));
                rest.Remove("WFNumberValue");
                rest.Remove("WFAnotherNumber");
            }
            return (sb.ToString(), rest);
        }

        var numericSide = Conditions.IsNumeric((int)code);
        if (numericSide && rest.Remove("WFNumberValue", out var number))
            sb.Append(' ').Append(Value(number, ParamKind.Number));
        else if (!numericSide && rest.TryGetValue("WFNumberValue", out var num) && num is IntegerValue or RealValue &&
                 !rest.ContainsKey("WFConditionalActionString"))
        {
            sb.Append(' ').Append(Value(num, ParamKind.Number));
            rest.Remove("WFNumberValue");
        }
        else if (!numericSide && rest.TryGetValue("WFConditionalActionString", out var text))
        {
            var printed = Value(text, ParamKind.Text);
            // A plain number literal would be parsed back as WFNumberValue.
            if (printed.Length > 0 && (printed[0] == '"' || printed.StartsWith("text(") || Lexer.IsIdentStart(printed[0])))
            {
                sb.Append(' ').Append(printed);
                rest.Remove("WFConditionalActionString");
            }
        }
        return (sb.ToString(), rest);
    }

    private void PrintAction(ActionInstance a)
    {
        var definition = _catalog.ById(a.Identifier);

        if (a.Identifier == "is.workflow.actions.comment" && a.Parameters.Count == 1 &&
            a.Get("WFCommentActionText") is StringValue { Value: var comment } && !comment.Contains('\n') &&
            comment.Trim() == comment)
        {
            Line("// " + comment);
            return;
        }

        if (a.Identifier is "is.workflow.actions.setvariable" or "is.workflow.actions.appendvariable" &&
            a.Get("WFVariableName") is StringValue { Value: var varName } && _scope.IsDeclaredVariable(varName) &&
            a.Get("WFInput") is { } input && !IsBound(a, out _, out _))
        {
            var rest = Leftover(a);
            rest.Remove("WFVariableName");
            rest.Remove("WFInput");
            rest.Remove(ActionInstance.UuidKey);
            var op = a.Identifier == "is.workflow.actions.setvariable" ? "=" : "+=";
            Line($"var {varName} {op} {Value(input, ParamKind.Variable)}{With(rest, definition)}");
            return;
        }

        if (definition is null || definition.Control != ControlFlowKind.None)
        {
            PrintGeneric(a, includeStructure: definition?.Control is not null and not ControlFlowKind.None);
            return;
        }

        var args = Leftover(a).ToList();
        var printedArgs = new List<string>();
        // Single catalog parameter that is the first one: print positionally.
        if (args.Count == 1 && definition.Params.Count > 0 && args[0].Key == definition.Params[0].Key)
            printedArgs.Add(Value(args[0].Value, definition.Params[0].Type));
        else
        {
            // Catalog order first, then unknown keys.
            var ordered = args.OrderBy(p =>
            {
                var idx = definition.Params.FindIndex(d => d.Key == p.Key);
                return idx < 0 ? int.MaxValue : idx;
            });
            printedArgs.AddRange(ordered.Select(p => NamedArg(p.Key, p.Value, definition)));
        }

        Line($"{Binding(a)}{definition.Dsl}({string.Join(", ", printedArgs)}){AsSuffix(a)}");
    }

    private string Binding(ActionInstance a) => IsBound(a, out _, out var name) ? name + " = " : "";

    private string AsSuffix(ActionInstance a) => IsBound(a, out var uuid, out _) ? AsClause(uuid, a) : "";

    private void PrintGeneric(ActionInstance a, bool includeStructure)
    {
        // The UUID is expressed by the "name =" binding; grouping keys only when printing blocks flat.
        var parameters = a.Parameters.Where(p => p.Key != ActionInstance.UuidKey &&
                                                 (includeStructure || !StructuralKeys.Contains(p.Key)));
        var args = string.Join(", ", parameters.Select(p => $"{NameOrString(p.Key)}: {Raw(p.Value)}"));
        Line($"{Binding(a)}action {Str(a.Identifier)} ({args}){AsSuffix(a)}");
    }

    private string NamedArg(string key, ParamValue value, ActionDefinition? definition)
    {
        var param = definition?.ParamByKey(key);
        if (param is not null) return $"{param.Name}: {Value(value, param.Type)}";
        // Unknown key: make sure it does not collide with a catalog parameter name.
        var keyText = definition?.FindParam(key) is not null ? Str(key) : NameOrString(key);
        return $"{keyText}: {Raw(value)}";
    }

    // ------------------------------------------------------------------ values

    /// <summary>
    /// Prints a value using the friendliest syntax for its kind, verified by parsing it back;
    /// falls back to the raw form when the friendly form would not round-trip exactly.
    /// </summary>
    private string Value(ParamValue value, ParamKind kind)
    {
        var friendly = Friendly(value, kind);
        if (friendly is not null && ParsesBackTo(friendly, kind, value)) return friendly;
        var raw = Raw(value);
        return ParsesBackTo(raw, kind, value) ? raw : $"raw({raw})";
    }

    private bool ParsesBackTo(string text, ParamKind kind, ParamValue expected)
    {
        try
        {
            return DslParser.ParseValue(text, kind, _scope).Equals(expected);
        }
        catch (DslException)
        {
            return false;
        }
    }

    private string? Friendly(ParamValue value, ParamKind kind) => value switch
    {
        TokenString t when kind == ParamKind.Text && t.Attachments.Count == 1 && t.Text == TokenString.Placeholder.ToString()
            => VariableExpr(t.Attachments[0].Variable),
        TokenString t when kind is ParamKind.Text or ParamKind.Variable or ParamKind.String or ParamKind.Enum
                             or ParamKind.Number or ParamKind.Integer => Template(t),
        DictionaryFieldValue f when kind == ParamKind.Dictionary => FriendlyFields(f),
        ArrayValue arr when kind == ParamKind.List => FriendlyList(arr),
        _ => null,
    };

    private string? FriendlyFields(DictionaryFieldValue f)
    {
        var entries = new List<string>();
        foreach (var item in f.Items)
        {
            var key = Template(item.Key);
            var value = FriendlyItem(item.ItemType, item.Value);
            if (value is null) return null;
            entries.Add($"{key}: {value}");
        }
        return entries.Count == 0 ? "{}" : "{ " + string.Join(", ", entries) + " }";
    }

    private string? FriendlyList(ArrayValue arr)
    {
        var items = new List<string>();
        foreach (var item in arr.Items)
        {
            if (item is not DictValue d || d.Entries.Count != 2 || d["WFItemType"] is not IntegerValue type || d["WFValue"] is not { } v)
                return null;
            var printed = FriendlyItem((DictionaryItemType)type.Value, v);
            if (printed is null) return null;
            items.Add(printed);
        }
        return "[" + string.Join(", ", items) + "]";
    }

    private string? FriendlyItem(DictionaryItemType type, ParamValue value) => (type, value) switch
    {
        (DictionaryItemType.Text, TokenString t) => t.Attachments.Count == 1 && t.Text == TokenString.Placeholder.ToString()
            ? VariableExpr(t.Attachments[0].Variable)
            : Template(t),
        (DictionaryItemType.Number, TokenString { Attachments.Count: 0 } t) when IsNumberLiteral(t.Text) => t.Text,
        (DictionaryItemType.Boolean, WrappedValue { SerializationType: "WFNumberSubstitutableState", Value: BoolValue b })
            => b.Value ? "true" : "false",
        (DictionaryItemType.Dictionary, DictionaryFieldValue f) => FriendlyFields(f),
        (DictionaryItemType.Array, WrappedValue { SerializationType: "WFArrayParameterState", Value: ArrayValue arr })
            => FriendlyList(arr),
        _ => null,
    };

    private static bool IsNumberLiteral(string s)
    {
        try
        {
            var tokens = Lexer.Tokenize(s);
            return tokens.Count == 2 && tokens[0].Kind == TokenKind.Number && tokens[0].Text == s;
        }
        catch (DslException) { return false; }
    }

    /// <summary>Lossless, kind-independent form.</summary>
    private string Raw(ParamValue value) => value switch
    {
        StringValue s => Str(s.Value),
        IntegerValue i => i.Value.ToString(CultureInfo.InvariantCulture),
        RealValue r => RealText(r.Value),
        BoolValue b => b.Value ? "true" : "false",
        DataValue d => $"data({Str(Convert.ToBase64String(d.Value))})",
        DateValue d => $"date({Str(d.Value.ToString("o", CultureInfo.InvariantCulture))})",
        ArrayValue a => "[" + string.Join(", ", a.Items.Select(Raw)) + "]",
        DictValue d => d.Entries.Count == 0 ? "{}" : "{ " + string.Join(", ", d.Entries.Select(e => $"{Str(e.Key)}: {Raw(e.Value)}")) + " }",
        TokenString t => $"text({Template(t)})",
        VariableValue v => VariableExpr(v.Variable),
        DictionaryFieldValue f => RawFields(f),
        WrappedValue w => $"wrap({Str(w.SerializationType)}, {Raw(w.Value)})",
        _ => throw new NotSupportedException(value.GetType().Name),
    };

    /// <summary>fields({…}) when it maps back exactly, otherwise the full wrapped plist structure.</summary>
    private string RawFields(DictionaryFieldValue f)
    {
        var friendly = FriendlyFields(f);
        if (friendly is not null)
        {
            var candidate = $"fields({friendly})";
            if (ParsesBackTo(candidate, ParamKind.Raw, f)) return candidate;
        }
        var ns = (Claunia.PropertyList.NSDictionary)PlistSerializer.ToNS(f);
        return $"wrap(\"WFDictionaryFieldValue\", {Raw(PlistSerializer.ToParam(ns["Value"]))})";
    }

    private static string RealText(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "0.0";
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";
    }

    private string Template(TokenString t)
    {
        var sb = new StringBuilder("\"");
        var byPosition = t.Attachments.ToDictionary(a => a.Position, a => a.Variable);
        for (var i = 0; i < t.Text.Length; i++)
        {
            var c = t.Text[i];
            if (c == TokenString.Placeholder && byPosition.TryGetValue(i, out var v))
            {
                sb.Append('{').Append(VariableExpr(v)).Append('}');
                continue;
            }
            AppendEscaped(sb, c);
        }
        return sb.Append('"').ToString();
    }

    private string VariableExpr(VariableRef v)
    {
        var name = _scope.NameOf(v);
        if (name is null && v.Kind == VariableKinds.ActionOutput && v.OutputUuid is { } uuid &&
            _namesByUuid.TryGetValue(uuid, out var magic) && _outputNameByUuid[uuid] == v.OutputName)
            name = magic;

        string text;
        if (name is not null) text = name;
        else if (v.Kind == VariableKinds.Variable && v.OutputUuid is null && v.OutputName is null && v.VariableName is { } vn)
            text = $"var({Str(vn)})";
        else if (v.Kind == VariableKinds.ActionOutput && v.OutputUuid is not null && v.VariableName is null)
            text = $"output({Str(v.OutputUuid)}, {Str(v.OutputName ?? "")})";
        else if (v.OutputUuid is null && v.OutputName is null && v.VariableName is null)
            text = $"global({Str(v.Kind)})";
        else
        {
            // Unusual combination: put everything except Type into .with().
            var extra = new List<KeyValuePair<string, ParamValue>>();
            if (v.OutputUuid is not null) extra.Add(new("OutputUUID", new StringValue(v.OutputUuid)));
            if (v.OutputName is not null) extra.Add(new("OutputName", new StringValue(v.OutputName)));
            if (v.VariableName is not null) extra.Add(new("VariableName", new StringValue(v.VariableName)));
            if (v.Extra is not null) extra.AddRange(v.Extra.Entries);
            return $"global({Str(v.Kind)}).with({Raw(new DictValue(extra))})";
        }

        if (v.Extra is null) return text;

        // Tapped-variable options (type / property / dictionary key) have a short form.
        if (Aggrandizements.TryDescribe(v, out var itemClass, out var property, out var dictionaryKey))
        {
            var sb = new StringBuilder(text);
            if (itemClass is not null)
                sb.Append(".as(").Append(Aggrandizements.TypeByClass(itemClass)?.Name ?? Str(itemClass)).Append(')');
            if (property is not null) sb.Append(".get(").Append(Str(property)).Append(')');
            if (dictionaryKey is not null) sb.Append('[').Append(Str(dictionaryKey)).Append(']');
            return sb.ToString();
        }
        return $"{text}.with({Raw(v.Extra)})";
    }

    public static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s) AppendEscaped(sb, c);
        return sb.Append('"').ToString();
    }

    private static void AppendEscaped(StringBuilder sb, char c)
    {
        switch (c)
        {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\r': sb.Append("\\r"); break;
            case '\t': sb.Append("\\t"); break;
            case '{': sb.Append("\\{"); break;
            case '}': sb.Append("\\}"); break;
            case TokenString.Placeholder: sb.Append("\\uFFFC"); break;
            default:
                if (char.IsControl(c)) sb.Append("\\u").Append(((int)c).ToString("X4"));
                else sb.Append(c);
                break;
        }
    }
}


