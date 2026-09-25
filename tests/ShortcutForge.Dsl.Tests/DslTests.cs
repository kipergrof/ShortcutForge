using System.Text;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;

namespace ShortcutForge.Dsl.Tests;

public class DslTests
{
    private const string Example = """
        #name "Reggeli"
        #icon color=blue glyph=59511
        #input WFStringContentItem, WFURLContentItem
        #type NCWidget

        // Köszönés
        greeting = Text("Jó reggelt, {ShortcutInput}!")
        var count = 3
        if greeting contains "reggel" {
            Notification(greeting, title: "Hello", sound: true)
        } else {
            Alert("Nincs reggel", title: "Hmm", cancel: false)
        }
        repeat count {
            Vibrate()
        }
        items = List(["alma", "körte", 5])
        foreach items {
            ShowResult("Elem: {RepeatItem} ({RepeatIndex})")
        }
        menu "Válassz" {
            case "Egy" {
                var result = "egy"
            }
            case "Kettő" {
                var result += "kettő"
            }
        } -> choice
        response = GetContentsOfUrl("https://example.com/api", method: "POST", bodyType: "JSON", json: { "name": greeting, "n": 42, "ok": true, "nested": { "a": "b" } })
        CopyToClipboard(response)
        action "com.example.app.MyIntent" (param: "x", count: 5, flag: false)
        """;

    [Fact]
    public void Parses_example()
    {
        var s = DslParser.Parse(Example);

        Assert.Equal("Reggeli", s.Name);
        Assert.Equal(463140863, s.Icon.StartColor);
        Assert.Equal(["WFStringContentItem", "WFURLContentItem"], s.InputClasses);
        Assert.Equal(["NCWidget"], s.Types);
        Assert.Null(ControlFlow.Validate(s.Actions));

        Assert.Equal("is.workflow.actions.comment", s.Actions[0].Identifier);
        var text = s.Actions[1];
        Assert.Equal("is.workflow.actions.gettext", text.Identifier);
        var token = Assert.IsType<TokenString>(text.Parameters["WFTextActionText"]);
        Assert.Equal("Jó reggelt, \uFFFC!", token.Text);
        Assert.Equal(VariableKinds.ShortcutInput, token.Attachments[0].Variable.Kind);

        var setVar = s.Actions[2];
        Assert.Equal("is.workflow.actions.setvariable", setVar.Identifier);
        Assert.Equal(new StringValue("count"), setVar.Parameters["WFVariableName"]);

        var ifStart = s.Actions[3];
        Assert.Equal(ControlFlowMode.Start, ifStart.ControlFlow);
        Assert.Equal(new IntegerValue(Conditions.Contains), ifStart.Parameters["WFCondition"]);
        var input = Assert.IsType<DictValue>(ifStart.Parameters["WFInput"]);
        var inputVar = Assert.IsType<VariableValue>(input["Variable"]);
        Assert.Equal(text.Uuid, inputVar.Variable.OutputUuid);
        Assert.Equal("Text", inputVar.Variable.OutputName);

        var notification = s.Actions[4];
        Assert.Equal(new BoolValue(true), notification.Parameters["WFNotificationActionSound"]);

        var request = s.Actions.Single(a => a.Identifier == "is.workflow.actions.downloadurl");
        var json = Assert.IsType<DictionaryFieldValue>(request.Parameters["WFJSONValues"]);
        Assert.Equal(4, json.Items.Count);
        Assert.Equal(DictionaryItemType.Number, json.Items[1].ItemType);
        Assert.Equal(DictionaryItemType.Boolean, json.Items[2].ItemType);
        Assert.Equal(DictionaryItemType.Dictionary, json.Items[3].ItemType);

        var custom = s.Actions.Last();
        Assert.Equal("com.example.app.MyIntent", custom.Identifier);
        Assert.Equal(new IntegerValue(5), custom.Parameters["count"]);

        // The plist can be written.
        Assert.NotEmpty(PlistSerializer.WriteBinary(s));
    }

    [Fact]
    public void Round_trips_example()
    {
        var first = DslParser.Parse(Example);
        var printed = DslPrinter.Print(first);
        var second = DslParser.Parse(printed);
        AssertEquivalent(first, second, printed);

        // Printing is stable.
        Assert.Equal(printed, DslPrinter.Print(second));
    }

    [Fact]
    public void Round_trips_plist_sample_with_raw_values()
    {
        var original = PlistSerializer.Read(Encoding.UTF8.GetBytes(Core.Tests.PlistSamples.SampleXml));
        var printed = DslPrinter.Print(original);
        var reparsed = DslParser.Parse(printed);
        AssertEquivalent(original, reparsed, printed);
    }

    [Fact]
    public void Round_trips_unusual_values()
    {
        var s = new Shortcut { Name = "Furcsa" };
        s.Extra["WFWorkflowOutputContentItemClasses"] = new ArrayValue([new StringValue("WFStringContentItem")]);
        var text = new ActionInstance("is.workflow.actions.gettext") { Uuid = "U1" };
        text.Parameters["WFTextActionText"] = new StringValue("plain string where text expected {braces} \"quotes\"");
        s.Actions.Add(text);

        var alert = new ActionInstance("is.workflow.actions.alert");
        alert.Parameters["WFAlertActionMessage"] = new VariableValue(
            VariableRef.Output("U1", "Text") with
            {
                Extra = new DictValue([new("Aggrandizements", new ArrayValue([
                    new DictValue([new("Type", new StringValue("WFPropertyVariableAggrandizement")), new("PropertyName", new StringValue("Name"))]),
                ]))]),
            });
        alert.Parameters["WFAlertActionTitle"] = new IntegerValue(12);
        alert.Parameters["Unknown Key"] = new ArrayValue([new RealValue(2.0), new BoolValue(true), new DataValue([1, 2, 3])]);
        alert.Parameters["title"] = new StringValue("collides with a DSL name");
        s.Actions.Add(alert);

        var generic = new ActionInstance("com.thirdparty.Intent");
        generic.Parameters["value"] = TokenString.Of(VariableRef.Named("Some Var With Spaces"));
        generic.Parameters["dict"] = new DictionaryFieldValue([
            new DictionaryField(DictionaryItemType.Number, TokenString.Plain("k"), TokenString.Plain("not a number")),
        ]);
        generic.Parameters["wrapped"] = new WrappedValue("WFQuantityFieldValue", new DictValue([new("Magnitude", new StringValue("5"))]));
        s.Actions.Add(generic);

        // Orphaned control flow is printed flat and survives.
        s.Actions.Add(new ActionInstance(ControlFlow.IfId) { GroupingIdentifier = "G", ControlFlow = ControlFlowMode.End });

        var printed = DslPrinter.Print(s);
        var reparsed = DslParser.Parse(printed);
        AssertEquivalent(s, reparsed, printed);
    }

    [Fact]
    public void Round_trips_every_catalog_block()
    {
        var s = new Shortcut();
        foreach (var def in ActionCatalog.Default.Actions)
        {
            if (def.Control != ControlFlowKind.None) s.Actions.AddRange(ControlFlow.CreateBlock(def));
            else s.Actions.Add(new ActionInstance(def.Id));
        }
        var printed = DslPrinter.Print(s);
        AssertEquivalent(s, DslParser.Parse(printed), printed);
    }

    [Theory]
    [InlineData("Alert(", "fájl vége")]
    [InlineData("Alert(\"a\"", "')'")]
    [InlineData("Foo()", "Ismeretlen akció")]
    [InlineData("Alert(nincs)", "Ismeretlen változó")]
    [InlineData("if x {", "Ismeretlen változó")]
    [InlineData("x = Text(\"a\")\nif x ~ 1 {}", "Váratlan karakter")]
    [InlineData("Text(\"a {\")", "Lezáratlan")]
    [InlineData("#icon color=nope", "Ismeretlen szín")]
    public void Reports_errors(string source, string expected)
    {
        var ex = Assert.Throws<DslException>(() => DslParser.Parse(source));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Variable_type_property_and_key_short_forms()
    {
        const string source = """
            data = GetContentsOfUrl("https://example.com")
            Alert(data.as(Dictionary)["név"])
            f = GetFile()
            ShowResult("Méret: {f.get("File Size")}")
            n = AskForInput(prompt: "Szám?")
            if n.as(Number) > 10 {
                Vibrate()
            }
            """;
        var s = DslParser.Parse(source);

        var alert = s.Actions[1];
        var message = Assert.IsType<TokenString>(alert.Parameters["WFAlertActionMessage"]);
        var v = Assert.Single(message.Attachments).Variable;
        Assert.True(Aggrandizements.TryDescribe(v, out var itemClass, out var property, out var key));
        Assert.Equal("WFDictionaryContentItem", itemClass);
        Assert.Null(property);
        Assert.Equal("név", key);

        var show = Assert.IsType<TokenString>(s.Actions[3].Parameters["Text"]);
        Assert.True(Aggrandizements.TryDescribe(show.Attachments[0].Variable, out _, out property, out _));
        Assert.Equal("File Size", property);

        var printed = DslPrinter.Print(s);
        Assert.Contains(".as(Dictionary)[\"név\"]", printed);
        Assert.Contains("{f.get(\"File Size\")}", printed.Replace("file.", "f."));
        Assert.Contains(".as(Number) > 10", printed);
        AssertEquivalent(s, DslParser.Parse(printed), printed);

        // Unknown type names are reported; raw class names are accepted.
        Assert.Throws<DslException>(() => DslParser.Parse("x = Text(\"a\")\nAlert(x.as(Nincs))"));
        var raw = DslParser.Parse("x = Text(\"a\")\nAlert(x.as(\"WFSomethingContentItem\"))");
        Assert.Contains(".as(\"WFSomethingContentItem\")", DslPrinter.Print(raw));
    }

    [Fact]
    public void Trailing_comments_are_ignored_but_own_line_comments_are_actions()
    {
        var s = DslParser.Parse("""
            // saját sor: Comment akció
            Alert("x") // sor végi megjegyzés
            Vibrate()  /* blokk */
            """);
        Assert.Equal(["is.workflow.actions.comment", "is.workflow.actions.alert", "is.workflow.actions.vibrate"],
            s.Actions.Select(a => a.Identifier));
    }

    [Fact]
    public void Messages_follow_the_ui_language()
    {
        try
        {
            Core.Localization.L.Language = "en";
            var ex = Assert.Throws<DslException>(() => DslParser.Parse("Alert(nincs)"));
            Assert.StartsWith("Unknown variable: nincs", ex.Message);
            Assert.Equal("Scripting", ActionCatalog.Default.ById(ControlFlow.IfId)!.DisplayCategory);
            Assert.Equal("Case sensitive", ActionCatalog.Default.ByDslName("ReplaceText")!.FindParam("caseSensitive")!.DisplayLabel);
        }
        finally
        {
            Core.Localization.L.Language = "hu";
        }
        Assert.StartsWith("Ismeretlen változó", Assert.Throws<DslException>(() => DslParser.Parse("Alert(nincs)")).Message);
    }

    [Fact]
    public void Template_helpers_round_trip()
    {
        var scope = new NameScope();
        scope.BindOutput("name", VariableRef.Output("U", "Text"));
        var t = DslParser.ParseTemplate("Szia {name}, \"idézet\" és {ShortcutInput}", scope);
        Assert.Equal(2, t.Attachments.Count);
        Assert.Equal("Szia {name}, \"idézet\" és {ShortcutInput}", DslPrinter.PrintTemplate(t, scope));

        // Variables with type / property / key inside text fields.
        var fancy = DslParser.ParseTemplate("Méret: {name.get(\"File Size\")} és {name.as(Dictionary)[\"k\"]} \"idézet\"", scope);
        Assert.Equal(2, fancy.Attachments.Count);
        Assert.Equal("Méret: {name.get(\"File Size\")} és {name.as(Dictionary)[\"k\"]} \"idézet\"", DslPrinter.PrintTemplate(fancy, scope));

        // A single variable is shown as {name}, not as a bare identifier.
        Assert.Equal("{name}", DslPrinter.PrintTemplate(TokenString.Of(VariableRef.Output("U", "Text")), scope));

        // Tricky characters survive a template round trip.
        foreach (var text in new[] { "a\\b", "C:\\path\\{x}", "új\nsor\ttab", "\\{nem változó\\}", "\"\"", "\uFFFC magányos" })
        {
            var original = TokenString.Plain(text.Replace("\\{", "{").Replace("\\}", "}"));
            var shown = DslPrinter.PrintTemplate(original, scope);
            Assert.Equal(original, DslParser.ParseTemplate(shown, scope));
        }
    }

    // ------------------------------------------------------------------ equivalence

    /// <summary>
    /// Compares two shortcuts ignoring the actual UUID / GroupingIdentifier values
    /// (they are regenerated on parse) and UUIDs nobody references.
    /// </summary>
    internal static void AssertEquivalent(Shortcut expected, Shortcut actual, string printed)
    {
        var e = Normalize(expected);
        var a = Normalize(actual);
        Assert.True(e.Count == a.Count, $"Akciószám eltér: {e.Count} vs {a.Count}\n{printed}");
        for (var i = 0; i < e.Count; i++)
            Assert.True(e[i] == a[i], $"#{i} eltér:\n várt:  {e[i]}\n kapott: {a[i]}\n\n{printed}");
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Icon, actual.Icon);
        Assert.Equal(expected.InputClasses, actual.InputClasses);
        Assert.Equal(expected.Types, actual.Types);
        foreach (var (key, value) in expected.Extra)
            if (!(key == "WFWorkflowImportQuestions" && value is ArrayValue { Items.Count: 0 }))
                Assert.Equal(value, actual.Extra[key]);
    }

    private static List<string> Normalize(Shortcut s)
    {
        var referenced = s.Actions.SelectMany(a => a.Parameters.Values.SelectMany(DslPrinter.EnumerateVariables))
            .Where(v => v.OutputUuid is not null).Select(v => v.OutputUuid!).ToHashSet();
        var ids = new Dictionary<string, string>();
        string Id(string raw) => ids.TryGetValue(raw, out var n) ? n : ids[raw] = "#" + ids.Count;

        var result = new List<string>();
        foreach (var action in s.Actions)
        {
            var sb = new StringBuilder(action.Identifier);
            foreach (var (key, value) in action.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (key == ActionInstance.UuidKey)
                {
                    if (value is StringValue u && referenced.Contains(u.Value)) sb.Append($" UUID={Id(u.Value)}");
                    continue;
                }
                if (key == ActionInstance.GroupingKey && value is StringValue g)
                {
                    sb.Append($" Group={Id("G:" + g.Value)}");
                    continue;
                }
                sb.Append($" {key}={Describe(value, Id)}");
            }
            result.Add(sb.ToString());
        }
        return result;
    }

    private static string Describe(ParamValue v, Func<string, string> id) => v switch
    {
        VariableValue vv => $"Var({DescribeVar(vv.Variable, id)})",
        TokenString t => $"Tok({t.Text.Replace('\uFFFC', '@')}|{string.Join(";", t.Attachments.Select(a => a.Position + ":" + DescribeVar(a.Variable, id)))})",
        ArrayValue arr => "[" + string.Join(",", arr.Items.Select(x => Describe(x, id))) + "]",
        DictValue d => "{" + string.Join(",", d.Entries.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + ":" + Describe(x.Value, id))) + "}",
        DictionaryFieldValue f => "Fields(" + string.Join(",", f.Items.Select(x => $"{x.ItemType}:{Describe(x.Key, id)}={Describe(x.Value, id)}")) + ")",
        WrappedValue w => $"Wrap({w.SerializationType},{Describe(w.Value, id)})",
        DataValue data => "Data(" + Convert.ToBase64String(data.Value) + ")",
        _ => v.ToString(),
    };

    private static string DescribeVar(VariableRef v, Func<string, string> id) =>
        $"{v.Kind}/{(v.OutputUuid is null ? "" : id(v.OutputUuid))}/{v.OutputName}/{v.VariableName}/{(v.Extra is null ? "" : Describe(v.Extra, id))}";
}
