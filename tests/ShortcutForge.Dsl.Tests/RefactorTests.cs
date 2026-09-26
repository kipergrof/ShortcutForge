using ShortcutForge.Core.Model;

namespace ShortcutForge.Dsl.Tests;

public class RefactorTests
{
    private const string Source = """
        greeting = Text("Hello World, {ShortcutInput}! hello again")
        var counter = 1
        var counter += greeting
        Alert("Counter: {counter}", title: "hello")
        d = Dictionary({ "hello": "world", "n": 2 })
        """;

    [Fact]
    public void Replaces_text_but_keeps_variables()
    {
        var s = DslParser.Parse(Source);

        var count = ShortcutRefactor.ReplaceText(s, "hello", "Szia");

        Assert.Equal(4, count); // "Hello" and "hello" in the text, the title, the dictionary key
        var text = Assert.IsType<TokenString>(s.Actions[0].Parameters["WFTextActionText"]);
        Assert.Equal("Szia World, ￼! Szia again", text.Text);
        Assert.Equal(VariableKinds.ShortcutInput, Assert.Single(text.Attachments).Variable.Kind);
        Assert.Equal(12, text.Attachments[0].Position);
        var dict = Assert.IsType<DictionaryFieldValue>(s.Actions[4].Parameters["WFItems"]);
        Assert.Equal("Szia", dict.Items[0].Key.Text);

        // Still a valid shortcut in the text view.
        var reparsed = DslParser.Parse(DslPrinter.Print(s));
        Assert.Equal(s.Actions.Count, reparsed.Actions.Count);
    }

    [Fact]
    public void Match_case_is_respected()
    {
        var s = DslParser.Parse(Source);
        Assert.Equal(1, ShortcutRefactor.ReplaceText(s, "Hello", "Hi", matchCase: true));
    }

    [Fact]
    public void Finds_actions_by_text_and_name()
    {
        var s = DslParser.Parse(Source);
        Assert.Equal([0, 3, 4], ShortcutRefactor.FindActions(s, "hello"));
        Assert.Equal([3], ShortcutRefactor.FindActions(s, "alert"));
        Assert.Empty(ShortcutRefactor.FindActions(s, "nincs ilyen"));
    }

    [Fact]
    public void Renames_named_variable_everywhere()
    {
        var s = DslParser.Parse(Source);
        Assert.Equal(["counter"], ShortcutRefactor.NamedVariables(s));

        var changed = ShortcutRefactor.RenameVariable(s, "counter", "Számláló");

        Assert.Equal(3, changed); // two Set/Add actions + one reference in the alert text
        Assert.Equal(["Számláló"], ShortcutRefactor.NamedVariables(s));
        var message = Assert.IsType<TokenString>(s.Actions[3].Parameters["WFAlertActionMessage"]);
        Assert.Equal("Számláló", message.Attachments[0].Variable.VariableName);
        Assert.Contains("var(\"Számláló\")", DslPrinter.Print(s).Replace("var Számláló", "var(\"Számláló\")"));
    }
}
