using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Dsl;

/// <summary>
/// Parses DSL source into a <see cref="Shortcut"/>. Syntax overview:
/// <code>
/// #name "Reggeli"
/// #icon color=blue glyph=59511
/// #input WFStringContentItem
/// // megjegyzés (Comment akció)
/// greeting = Text("Jó reggelt, {ShortcutInput}!")
/// var count = 3
/// if greeting contains "reggel" { Alert(greeting, title: "Hello") } else { Vibrate() }
/// repeat count { Vibrate() }
/// foreach list { ShowResult(RepeatItem) }
/// menu "Válassz" { case "A" { … } case "B" { … } } -> choice
/// action "com.example.intent" (Key: "value")
/// </code>
/// </summary>
public sealed class DslParser
{
    private readonly List<Token> _tokens;
    private readonly ActionCatalog _catalog;
    private readonly NameScope _scope;
    private readonly List<ActionInstance> _actions;
    private int _p;

    private DslParser(List<Token> tokens, ActionCatalog catalog, NameScope scope, List<ActionInstance> actions)
    {
        _tokens = tokens;
        _catalog = catalog;
        _scope = scope;
        _actions = actions;
    }

    public static Shortcut Parse(string source, ActionCatalog? catalog = null)
    {
        var tokens = Lexer.Tokenize(source);
        var scope = new NameScope();
        DeclareNamedVariables(tokens, scope);
        var shortcut = new Shortcut { Actions = [] };
        var parser = new DslParser(tokens, catalog ?? ActionCatalog.Default, scope, shortcut.Actions);
        parser.ParseProgram(shortcut);
        return shortcut;
    }

    /// <summary>Parses a single value expression (used by the visual editor's fields).</summary>
    public static ParamValue ParseValue(string source, ParamKind kind, NameScope scope)
    {
        var tokens = Lexer.Tokenize(source).Where(t => t.Kind != TokenKind.NewLine).ToList();
        var parser = new DslParser(tokens, ActionCatalog.Default, scope, []);
        var expr = parser.ParseExpr();
        parser.Expect(TokenKind.End);
        return ValueConverter.Convert(expr, kind);
    }

    /// <summary>Parses the inside of a text field, e.g. <c>Hello {name}</c> (no surrounding quotes).</summary>
    public static TokenString ParseTemplate(string template, NameScope scope)
    {
        // Inverse of DslPrinter.PrintTemplate: quotes, backslashes and control characters are
        // literal; only \{ and \} stay escaped braces.
        var sb = new System.Text.StringBuilder("\"");
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '\\' && i + 1 < template.Length && template[i + 1] is '{' or '}')
            {
                sb.Append(c).Append(template[++i]);
                continue;
            }
            if (c == '{')
            {
                var end = FindInterpolationEnd(template, i);
                if (LooksLikeRegexQuantifier(template, i + 1, end - 1))
                {
                    // "{2,4}"-style regex repetition count: never a valid variable expression
                    // (a comma can't appear inside one), so it can only be literal pattern text
                    // someone typed into a Match Text pattern field. Keep it as-is rather than
                    // failing to parse it as an interpolation.
                    for (var j = i; j < end; j++)
                        sb.Append(template[j] switch
                        {
                            '\\' => "\\\\",
                            '"' => "\\\"",
                            '{' => "\\{",
                            '}' => "\\}",
                            _ => template[j].ToString(),
                        });
                    i = end - 1;
                    continue;
                }
                // Interpolated expression: copied verbatim (it may contain quoted strings).
                sb.Append(template, i, end - i);
                i = end - 1;
                continue;
            }
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }
        var value = ParseValue(sb.Append('"').ToString(), ParamKind.Text, scope);
        return (TokenString)value;
    }

    /// <summary>
    /// True for the content of a "{...}" span shaped like a regex repetition quantifier
    /// (<c>{n}</c>, <c>{n,}</c>, <c>{n,m}</c>) — syntax a DSL value expression can never produce,
    /// since a bare comma isn't valid there, so it can't be a genuine variable interpolation.
    /// </summary>
    private static bool LooksLikeRegexQuantifier(string text, int start, int end)
    {
        if (end <= start) return false;
        var sawDigit = false;
        var sawComma = false;
        for (var i = start; i < end; i++)
        {
            var c = text[i];
            if (char.IsDigit(c)) { sawDigit = true; continue; }
            if (c == ',' && !sawComma) { sawComma = true; continue; }
            return false;
        }
        return sawDigit && sawComma;
    }

    /// <summary>Index just past the '}' matching the '{' at <paramref name="start"/> (or the end of text).</summary>
    private static int FindInterpolationEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}' when --depth == 0: return i + 1;
            }
        }
        return text.Length;
    }

    /// <summary>Named variables can be used before (e.g. in a loop) or without their <c>var</c> line.</summary>
    private static void DeclareNamedVariables(List<Token> tokens, NameScope scope)
    {
        for (var i = 0; i + 1 < tokens.Count; i++)
            if (tokens[i].Is("var") && tokens[i].Kind == TokenKind.Identifier && tokens[i + 1].Kind == TokenKind.Identifier)
                scope.DeclareVariable(tokens[i + 1].Text);
    }

    // ------------------------------------------------------------------ token helpers

    private Token Current => _tokens[_p];
    private Token PeekToken(int ahead = 1) => _tokens[Math.Min(_p + ahead, _tokens.Count - 1)];

    private Token Advance()
    {
        var t = _tokens[_p];
        if (_p < _tokens.Count - 1) _p++;
        return t;
    }

    private bool Accept(string symbolOrWord)
    {
        if (!Current.Is(symbolOrWord)) return false;
        Advance();
        return true;
    }

    private Token Expect(string symbolOrWord)
    {
        if (!Current.Is(symbolOrWord)) throw Error(L.T($"'{symbolOrWord}' kellene, de {Current} jött.", $"Expected '{symbolOrWord}', but found {Current}."));
        return Advance();
    }

    private Token Expect(TokenKind kind)
    {
        if (Current.Kind != kind) throw Error(L.T($"{KindName(kind)} kellene, de {Current} jött.", $"Expected {KindName(kind)}, but found {Current}."));
        return Advance();
    }

    private static string KindName(TokenKind kind) => kind switch
    {
        TokenKind.Identifier => L.T("név", "name"),
        TokenKind.String => L.T("szöveg", "text"),
        TokenKind.Number => L.T("szám", "number"),
        TokenKind.End => L.T("fájl vége", "end of file"),
        TokenKind.NewLine => L.T("sortörés", "line break"),
        _ => kind.ToString(),
    };

    private DslException Error(string message, Token? at = null)
    {
        var t = at ?? Current;
        return new DslException(message, t.Pos, t.Offset, Math.Max(1, t.Length));
    }

    private void SkipNewLines()
    {
        while (Current.Kind == TokenKind.NewLine || Current.Is(";")) Advance();
    }

    private void EndOfStatement()
    {
        if (Current.Kind is TokenKind.NewLine or TokenKind.End || Current.Is(";") || Current.Is("}")) return;
        throw Error(L.T($"Az utasítás végét vártam, de {Current} jött.", $"Expected the end of the statement, but found {Current}."));
    }

    // ------------------------------------------------------------------ statements

    private void ParseProgram(Shortcut shortcut)
    {
        SkipNewLines();
        while (Current.Kind != TokenKind.End)
        {
            if (Current.Kind == TokenKind.Directive) ParseDirective(shortcut);
            else ParseStatement();
            SkipNewLines();
        }
    }

    private void ParseBlock()
    {
        Expect("{");
        SkipNewLines();
        while (!Current.Is("}"))
        {
            if (Current.Kind == TokenKind.End) throw Error(L.T("Hiányzó '}'.", "Missing '}'."));
            ParseStatement();
            SkipNewLines();
        }
        Expect("}");
    }

    private void ParseStatement()
    {
        var t = Current;
        switch (t.Kind)
        {
            case TokenKind.LineComment:
                Advance();
                Add(new ActionInstance("is.workflow.actions.comment")
                {
                    Parameters = { ["WFCommentActionText"] = new StringValue(t.Text) },
                });
                return;
            case TokenKind.Directive:
                throw Error(L.T("Direktíva (#…) csak a legfelső szinten állhat.", "Directives (#…) are only allowed at the top level."));
            case TokenKind.Identifier:
                break;
            default:
                throw Error(L.T($"Utasítást vártam, de {t} jött.", $"Expected a statement, but found {t}."));
        }

        switch (t.Text)
        {
            case "if": ParseIf(); return;
            case "repeat": ParseRepeat(); return;
            case "foreach": ParseForEach(); return;
            case "menu": ParseMenu(); return;
            case "var": ParseVar(); return;
        }

        // name = Action(...)
        if (PeekToken().Is("="))
        {
            var nameToken = Advance();
            Advance();
            if (!NameScope.IsIdentifier(nameToken.Text))
                throw Error(L.T($"'{nameToken.Text}' nem használható változónévként.", $"'{nameToken.Text}' cannot be used as a variable name."), nameToken);
            if (_scope.IsDeclaredVariable(nameToken.Text))
                throw Error(L.T($"'{nameToken.Text}' már elnevezett változó (var); válassz más nevet.", $"'{nameToken.Text}' is already a named variable (var); choose another name."), nameToken);
            var action = ParseActionCall();
            BindOutput(nameToken.Text, action, ParseOptionalAs());
            EndOfStatement();
            return;
        }

        ParseActionCall();
        EndOfStatement();
    }

    private string? ParseOptionalAs()
    {
        if (!Accept("as")) return null;
        var s = Expect(TokenKind.String);
        return PlainString(s);
    }

    private void BindOutput(string name, ActionInstance action, string? outputName)
    {
        var uuid = action.EnsureUuid();
        outputName ??= action.CustomOutputName ?? _catalog.ById(action.Identifier)?.Output ?? "Output";
        _scope.BindOutput(name, VariableRef.Output(uuid, outputName));
    }

    private void Add(ActionInstance action)
    {
        if (action.Uuid is null && _catalog.ById(action.Identifier)?.Output is not null) action.EnsureUuid();
        _actions.Add(action);
    }

    private ActionInstance ParseActionCall()
    {
        var nameToken = Expect(TokenKind.Identifier);
        ActionDefinition? definition;
        string identifier;
        if (nameToken.Text == "action")
        {
            identifier = PlainString(Expect(TokenKind.String));
            definition = _catalog.ById(identifier);
        }
        else
        {
            definition = _catalog.ByDslName(nameToken.Text)
                         ?? throw Error(L.T($"Ismeretlen akció: {nameToken.Text}. Ismeretlen akciókhoz használd: action \"azonosító\" (…)", $"Unknown action: {nameToken.Text}. For other actions use: action \"identifier\" (…)"), nameToken);
            if (definition.Control != ControlFlowKind.None)
                throw Error(L.T($"A(z) {definition.Name} blokk; használd az if / repeat / foreach / menu utasítást.", $"{definition.Name} is a block; use the if / repeat / foreach / menu statement."), nameToken);
            identifier = definition.Id;
        }

        var action = new ActionInstance(identifier);
        if (Current.Is("("))
            ParseArguments(action, definition, positionalAllowed: nameToken.Text != "action", rawOnly: nameToken.Text == "action");
        else if (nameToken.Text != "action")
            throw Error(L.T("Hiányzó '(' az akció neve után.", "Missing '(' after the action name."));
        Add(action);
        return action;
    }

    /// <summary>Parses <c>(value, name: value, "Raw Key": value)</c> into the action's parameters.</summary>
    private void ParseArguments(ActionInstance action, ActionDefinition? definition, bool positionalAllowed, bool rawOnly = false, bool allowOverride = false)
    {
        Expect("(");
        var position = 0;
        SkipNewLines();
        while (!Current.Is(")"))
        {
            string? key = null;
            ParamKind kind = ParamKind.Raw;
            var argToken = Current;

            var isNamed = (Current.Kind is TokenKind.Identifier or TokenKind.String) && PeekToken().Is(":");
            if (isNamed)
            {
                var keyToken = Advance();
                var keyText = keyToken.Kind == TokenKind.String ? PlainString(keyToken) : keyToken.Text;
                Advance(); // :
                // Quoted keys are raw plist keys; bare names may be DSL parameter names.
                var param = rawOnly ? null
                    : keyToken.Kind == TokenKind.String ? definition?.ParamByKey(keyText) : definition?.FindParam(keyText);
                if (param is not null) { key = param.Key; kind = param.Type; }
                else key = keyText;
            }
            else
            {
                if (!positionalAllowed || definition is null)
                    throw Error(L.T("Itt csak név szerinti paraméter adható meg (Kulcs: érték).", "Only named parameters are allowed here (Key: value)."));
                var candidates = definition.Params;
                if (position >= candidates.Count)
                    throw Error(L.T($"Túl sok paraméter: a(z) {definition.Name} akciónak {candidates.Count} paramétere van.", $"Too many parameters: {definition.Name} has {candidates.Count} parameters."));
                key = candidates[position].Key;
                kind = candidates[position].Type;
                position++;
            }

            if (!allowOverride && action.Parameters.ContainsKey(key))
                throw Error(L.T($"A(z) '{key}' paraméter kétszer szerepel.", $"Parameter '{key}' is given twice."), argToken);
            action.Parameters[key] = ValueConverter.Convert(ParseExpr(), kind);

            SkipNewLines();
            if (!Accept(",")) break;
            SkipNewLines();
        }
        SkipNewLines();
        Expect(")");
    }

    /// <summary>Optional <c>with (Key: value, …)</c> clause for extra raw parameters of a block.</summary>
    private void ParseWith(ActionInstance action, ActionDefinition? definition)
    {
        if (Accept("with")) ParseArguments(action, definition, positionalAllowed: false, allowOverride: true);
    }

    private (ActionInstance Start, string Group) StartBlock(string id)
    {
        var group = ActionInstance.NewUuid();
        var start = new ActionInstance(id) { GroupingIdentifier = group, ControlFlow = ControlFlowMode.Start };
        _actions.Add(start);
        return (start, group);
    }

    private ActionInstance ControlAction(string id, string group, ControlFlowMode mode)
    {
        var a = new ActionInstance(id) { GroupingIdentifier = group, ControlFlow = mode };
        _actions.Add(a);
        return a;
    }

    private void EndBlock(string id, string group)
    {
        var end = ControlAction(id, group, ControlFlowMode.End);
        end.EnsureUuid();
        if (Accept("->"))
        {
            var name = Expect(TokenKind.Identifier);
            if (!NameScope.IsIdentifier(name.Text)) throw Error(L.T($"'{name.Text}' nem használható változónévként.", $"'{name.Text}' cannot be used as a variable name."), name);
            BindOutput(name.Text, end, ParseOptionalAs());
        }
        EndOfStatement();
    }

    private void ParseIf()
    {
        Expect("if");
        var definition = _catalog.ById(ControlFlow.IfId);
        var (start, group) = StartBlock(ControlFlow.IfId);
        ParseCondition(start);
        ParseWith(start, definition);
        ParseBlock();
        SkipNewLinesBefore("else");
        if (Accept("else"))
        {
            var middle = ControlAction(ControlFlow.IfId, group, ControlFlowMode.Middle);
            ParseWith(middle, definition);
            if (Current.Is("if"))
            {
                // else if … → nested If inside Otherwise.
                ParseIf();
            }
            else ParseBlock();
        }
        EndBlock(ControlFlow.IfId, group);
    }

    /// <summary>Allows "}\nelse {" by looking past newlines for the given keyword.</summary>
    private void SkipNewLinesBefore(string keyword)
    {
        var p = _p;
        while (_tokens[p].Kind == TokenKind.NewLine && p < _tokens.Count - 1) p++;
        if (_tokens[p].Is(keyword)) _p = p;
    }

    private static readonly Dictionary<string, int> Operators = new()
    {
        ["=="] = Conditions.Is, ["!="] = Conditions.IsNot,
        ["<"] = Conditions.LessThan, ["<="] = Conditions.LessOrEqual,
        [">"] = Conditions.GreaterThan, [">="] = Conditions.GreaterOrEqual,
        ["contains"] = Conditions.Contains, ["beginsWith"] = Conditions.BeginsWith,
        ["endsWith"] = Conditions.EndsWith, ["hasValue"] = Conditions.HasAnyValue,
        ["between"] = Conditions.IsBetween,
    };

    private static readonly Dictionary<string, int> NegatedOperators = new()
    {
        ["contains"] = Conditions.DoesNotContain, ["hasValue"] = Conditions.DoesNotHaveAnyValue,
    };

    private void ParseCondition(ActionInstance start)
    {
        // Left side: a variable, or '_' for "no input".
        if (!Accept("_"))
        {
            var lhs = ParseExpr();
            if (lhs is not VariableExpr v) throw new DslException(L.T("A feltétel bal oldalán változó kell legyen.", "The left side of a condition must be a variable."), lhs.Pos);
            start.Parameters["WFInput"] = new DictValue([
                new("Type", new StringValue("Variable")),
                new("Variable", new VariableValue(v.Variable)),
            ]);
        }

        // Operator
        int code;
        var opToken = Current;
        if (Accept("!"))
        {
            var word = Expect(TokenKind.Identifier);
            if (!NegatedOperators.TryGetValue(word.Text, out code))
                throw Error(L.T($"Ismeretlen tagadott feltétel: !{word.Text}", $"Unknown negated condition: !{word.Text}"), word);
        }
        else if (Accept("?"))
        {
            var number = Expect(TokenKind.Number);
            if (!int.TryParse(number.Text, out code)) throw Error(L.T("Hibás feltétel kód.", "Invalid condition code."), number);
        }
        else if (Operators.TryGetValue(Current.Text, out code) && Current.Kind is TokenKind.Symbol or TokenKind.Identifier)
        {
            Advance();
        }
        else throw Error(L.T($"Feltétel operátort vártam (==, !=, <, >, contains, hasValue, …), de {opToken} jött.", $"Expected a condition operator (==, !=, <, >, contains, hasValue, …), but found {opToken}."));

        start.Parameters["WFCondition"] = new IntegerValue(code);
        if (Conditions.IsUnary(code) || Current.Is("{") || Current.Is("with")) return;

        var rhs = ParseExpr();
        if (code == Conditions.IsBetween)
        {
            start.Parameters["WFNumberValue"] = ValueConverter.Convert(rhs, ParamKind.Number);
            Expect("and");
            start.Parameters["WFAnotherNumber"] = ValueConverter.Convert(ParseExpr(), ParamKind.Number);
        }
        else if (Conditions.IsNumeric(code) || rhs is NumberExpr)
            start.Parameters["WFNumberValue"] = ValueConverter.Convert(rhs, ParamKind.Number);
        else
            start.Parameters["WFConditionalActionString"] = ValueConverter.Convert(rhs, ParamKind.Text);
    }

    private void ParseRepeat()
    {
        Expect("repeat");
        var (start, group) = StartBlock(ControlFlow.RepeatId);
        if (!Current.Is("{") && !Current.Is("with"))
            start.Parameters["WFRepeatCount"] = ValueConverter.Convert(ParseExpr(), ParamKind.Number);
        ParseWith(start, _catalog.ById(ControlFlow.RepeatId));
        ParseBlock();
        EndBlock(ControlFlow.RepeatId, group);
    }

    private void ParseForEach()
    {
        Expect("foreach");
        var (start, group) = StartBlock(ControlFlow.RepeatEachId);
        if (!Current.Is("{") && !Current.Is("with"))
            start.Parameters["WFInput"] = ValueConverter.Convert(ParseExpr(), ParamKind.Variable);
        ParseWith(start, _catalog.ById(ControlFlow.RepeatEachId));
        ParseBlock();
        EndBlock(ControlFlow.RepeatEachId, group);
    }

    private void ParseMenu()
    {
        Expect("menu");
        var definition = _catalog.ById(ControlFlow.MenuId);
        var (start, group) = StartBlock(ControlFlow.MenuId);
        if (!Current.Is("{") && !Current.Is("with"))
            start.Parameters["WFMenuPrompt"] = ValueConverter.Convert(ParseExpr(), ParamKind.Text);
        ParseWith(start, definition);

        Expect("{");
        SkipNewLines();
        var titles = new List<ParamValue>();
        while (!Current.Is("}"))
        {
            Expect("case");
            var titleToken = Current;
            var title = ParseExpr();
            var middle = ControlAction(ControlFlow.MenuId, group, ControlFlowMode.Middle);
            var titleValue = ValueConverter.Convert(title, ParamKind.String);
            if (titleValue is not StringValue titleString)
                throw Error(L.T("A menüpont neve egyszerű szöveg kell legyen.", "A menu item title must be plain text."), titleToken);
            middle.Parameters["WFMenuItemTitle"] = titleString;
            titles.Add(titleString);
            ParseWith(middle, definition);
            ParseBlock();
            SkipNewLines();
        }
        Expect("}");
        if (!start.Parameters.ContainsKey("WFMenuItems"))
            start.Parameters["WFMenuItems"] = new ArrayValue(titles);
        EndBlock(ControlFlow.MenuId, group);
    }

    private void ParseVar()
    {
        Expect("var");
        string name;
        var nameToken = Current;
        if (Current.Kind == TokenKind.Identifier) name = Advance().Text;
        else throw Error(L.T("Változónevet vártam a 'var' után.", "Expected a variable name after 'var'."));
        if (NameScope.Keywords.Contains(name) || NameScope.Builtins.ContainsKey(name))
            throw Error(L.T($"'{name}' foglalt név.", $"'{name}' is a reserved name."), nameToken);

        string id;
        if (Accept("+=")) id = "is.workflow.actions.appendvariable";
        else if (Accept("=")) id = "is.workflow.actions.setvariable";
        else throw Error(L.T("'=' vagy '+=' kellene a változónév után.", "Expected '=' or '+=' after the variable name."));

        var action = new ActionInstance(id);
        action.Parameters["WFVariableName"] = new StringValue(name);
        action.Parameters["WFInput"] = ValueConverter.Convert(ParseExpr(), ParamKind.Variable);
        ParseWith(action, _catalog.ById(id));
        Add(action);
        EndOfStatement();
    }

    private void ParseDirective(Shortcut shortcut)
    {
        var d = Advance();
        switch (d.Text)
        {
            case "name":
                shortcut.Name = PlainString(Expect(TokenKind.String));
                break;
            case "version":
                shortcut.ClientVersion = PlainString(Expect(TokenKind.String));
                break;
            case "icon":
            {
                long color = shortcut.Icon.StartColor, glyph = shortcut.Icon.GlyphNumber;
                while (Current.Kind == TokenKind.Identifier)
                {
                    var key = Advance();
                    Expect("=");
                    var value = Advance();
                    switch (key.Text)
                    {
                        case "color" when value.Kind == TokenKind.Identifier:
                            color = ShortcutIcon.ColorByName(value.Text) ?? throw Error(L.T($"Ismeretlen szín: {value.Text}", $"Unknown color: {value.Text}"), value);
                            break;
                        case "color" when value.Kind == TokenKind.Number:
                            if (!long.TryParse(value.Text, out color))
                                throw Error(L.T($"Hibás szín érték: {value.Text}", $"Invalid color value: {value.Text}"), value);
                            break;
                        case "glyph" when value.Kind == TokenKind.Number:
                            if (!long.TryParse(value.Text, out glyph))
                                throw Error(L.T($"Hibás glyph szám: {value.Text}", $"Invalid glyph number: {value.Text}"), value);
                            break;
                        default:
                            throw Error(L.T($"Hibás ikon beállítás: {key.Text}", $"Invalid icon setting: {key.Text}"), key);
                    }
                }
                shortcut.Icon = new ShortcutIcon(color, glyph);
                break;
            }
            case "input":
                shortcut.InputClasses = ParseNameList();
                break;
            case "type":
                shortcut.Types = ParseNameList();
                break;
            case "meta":
            {
                var keyToken = Advance();
                var key = keyToken.Kind == TokenKind.String ? PlainString(keyToken) : keyToken.Text;
                Expect("=");
                shortcut.Extra[key] = ValueConverter.Raw(ParseExpr());
                break;
            }
            default:
                throw Error(L.T($"Ismeretlen direktíva: #{d.Text}", $"Unknown directive: #{d.Text}"), d);
        }
        EndOfStatement();
    }

    private List<string> ParseNameList()
    {
        var list = new List<string>();
        while (Current.Kind is TokenKind.Identifier or TokenKind.String)
        {
            var t = Advance();
            list.Add(t.Kind == TokenKind.String ? PlainString(t) : t.Text);
            if (!Accept(",")) break;
        }
        return list;
    }

    private string PlainString(Token t)
    {
        if (t.Parts!.Any(p => p is InterpolationPart)) throw Error(L.T("Itt nem lehet változó a szövegben.", "Variables are not allowed in this text."), t);
        return string.Concat(t.Parts!.OfType<TextPart>().Select(p => p.Text));
    }

    // ------------------------------------------------------------------ expressions

    private Expr ParseExpr()
    {
        var t = Current;
        switch (t.Kind)
        {
            case TokenKind.String:
                Advance();
                return ParseStringToken(t);
            case TokenKind.Number:
                Advance();
                return new NumberExpr(t.Pos, t.Text, !t.Text.Contains('.') && !t.Text.Contains('e') && !t.Text.Contains('E'));
            case TokenKind.Identifier:
                return ParseIdentifierExpr();
            case TokenKind.Symbol when t.Text == "[":
                return ParseList();
            case TokenKind.Symbol when t.Text == "{":
                return ParseDict();
            default:
                throw Error(L.T($"Értéket vártam, de {t} jött.", $"Expected a value, but found {t}."));
        }
    }

    private StringExpr ParseStringToken(Token t)
    {
        var parts = new List<object>();
        foreach (var part in t.Parts!)
        {
            if (part is TextPart tp) parts.Add(tp.Text);
            else if (part is InterpolationPart ip)
            {
                var sub = new DslParser(ip.Tokens.ToList(), _catalog, _scope, _actions);
                var expr = sub.ParseExpr();
                sub.Expect(TokenKind.End);
                if (expr is not VariableExpr v)
                    throw new DslException(L.T("A {…} közé csak változó kerülhet.", "Only a variable can go inside {…}."), ip.Pos);
                parts.Add(v.Variable);
            }
        }
        return new StringExpr(t.Pos, parts);
    }

    private Expr ParseIdentifierExpr()
    {
        var t = Advance();
        switch (t.Text)
        {
            case "true": return new BoolExpr(t.Pos, true);
            case "false": return new BoolExpr(t.Pos, false);
        }

        VariableRef variable;
        if (NameScope.Forms.Contains(t.Text) && Current.Is("("))
        {
            var args = ParseFormArgs();
            switch (t.Text)
            {
                case "var":
                    if (args is not [StringExpr { HasInterpolation: false } vn])
                        throw Error(L.T("var(\"név\") formát vártam.", "Expected the form var(\"name\")."), t);
                    variable = VariableRef.Named(vn.PlainText);
                    break;
                case "output":
                    if (args is not [StringExpr { HasInterpolation: false } uuid, StringExpr { HasInterpolation: false } oname])
                        throw Error(L.T("output(\"UUID\", \"név\") formát vártam.", "Expected the form output(\"UUID\", \"name\")."), t);
                    variable = VariableRef.Output(uuid.PlainText, oname.PlainText);
                    break;
                default:
                    return new FormExpr(t.Pos, t.Text, args);
            }
        }
        else if (t.Text == "global" && Current.Is("("))
        {
            // global("Type") – any other attachment type, kept verbatim.
            var args = ParseFormArgs();
            if (args is not [StringExpr { HasInterpolation: false } kind]) throw Error(L.T("global(\"Típus\") formát vártam.", "Expected the form global(\"Type\")."), t);
            variable = VariableRef.Global(kind.PlainText);
        }
        else
        {
            variable = _scope.Resolve(t.Text)
                       ?? throw Error(L.T($"Ismeretlen változó: {t.Text}. Előbb rendeld hozzá (x = Akció(…)) vagy használd a var kulcsszót.", $"Unknown variable: {t.Text}. Assign it first (x = Action(…)) or use the var keyword."), t);
        }

        // Suffixes, as when tapping a variable in the Shortcuts app:
        //   .as(Type)   get as type      .get("Property")   property      ["key"]   dictionary value
        //   .with({…})  any extra keys (raw)
        while (true)
        {
            if (Current.Is("["))
            {
                Advance();
                var keyToken = Expect(TokenKind.String);
                Expect("]");
                variable = Aggrandizements.Append(variable, Aggrandizements.DictionaryValue(PlainString(keyToken)));
                continue;
            }
            if (!Current.Is(".")) break;
            var member = PeekToken();
            switch (member.Text)
            {
                case "with":
                {
                    Advance(); Advance();
                    var args = ParseFormArgs();
                    if (args is not [DictExpr extra]) throw Error(L.T(".with({…}) egy szótárat vár.", ".with({…}) expects a dictionary."), t);
                    var extraValue = (DictValue)ValueConverter.Raw(extra);
                    variable = variable with { Extra = extraValue };
                    continue;
                }
                case "as":
                {
                    Advance(); Advance();
                    Expect("(");
                    var typeToken = Advance();
                    string itemClass;
                    if (typeToken.Kind == TokenKind.String) itemClass = PlainString(typeToken);
                    else if (typeToken.Kind == TokenKind.Identifier && Aggrandizements.TypeByName(typeToken.Text) is { } type) itemClass = type.ItemClass;
                    else throw Error(L.F("Ismeretlen típus: {0}. Lehetséges: {1}", "Unknown type: {0}. Possible: {1}",
                        typeToken.Text, string.Join(", ", Aggrandizements.Types.Select(x => x.Name))), typeToken);
                    Expect(")");
                    variable = Aggrandizements.Append(variable, Aggrandizements.Coercion(itemClass));
                    continue;
                }
                case "get":
                {
                    Advance(); Advance();
                    Expect("(");
                    var property = PlainString(Expect(TokenKind.String));
                    Expect(")");
                    variable = Aggrandizements.Append(variable, Aggrandizements.Property(property));
                    continue;
                }
            }
            break;
        }

        return new VariableExpr(t.Pos, variable);
    }

    private List<Expr> ParseFormArgs()
    {
        Expect("(");
        var args = new List<Expr>();
        SkipNewLines();
        while (!Current.Is(")"))
        {
            args.Add(ParseExpr());
            SkipNewLines();
            if (!Accept(",")) break;
            SkipNewLines();
        }
        Expect(")");
        return args;
    }

    private ListExpr ParseList()
    {
        var start = Expect("[");
        var items = new List<Expr>();
        SkipNewLines();
        while (!Current.Is("]"))
        {
            items.Add(ParseExpr());
            SkipNewLines();
            if (!Accept(",")) break;
            SkipNewLines();
        }
        Expect("]");
        return new ListExpr(start.Pos, items);
    }

    private DictExpr ParseDict()
    {
        var start = Expect("{");
        var entries = new List<(Expr, Expr)>();
        SkipNewLines();
        while (!Current.Is("}"))
        {
            var key = ParseExpr();
            Expect(":");
            SkipNewLines();
            var value = ParseExpr();
            entries.Add((key, value));
            SkipNewLines();
            if (!Accept(",")) break;
            SkipNewLines();
        }
        Expect("}");
        return new DictExpr(start.Pos, entries);
    }
}

