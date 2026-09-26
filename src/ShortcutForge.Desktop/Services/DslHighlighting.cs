using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ShortcutForge.Desktop.Services;

/// <summary>Syntax highlighting of the text language (same rules and colours as the Windows app).</summary>
public static class DslHighlighting
{
    private const string Xshd = """
        <SyntaxDefinition name="ShortcutForge" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="{Comment}" fontStyle="italic" />
          <Color name="Directive" foreground="{Directive}" fontWeight="bold" />
          <Color name="String" foreground="{String}" />
          <Color name="Interpolation" foreground="{Interpolation}" fontWeight="bold" />
          <Color name="Keyword" foreground="{Keyword}" fontWeight="bold" />
          <Color name="Number" foreground="{Number}" />
          <Color name="Action" foreground="{Action}" fontWeight="bold" />
          <Color name="Builtin" foreground="{Builtin}" />
          <Color name="Form" foreground="{Form}" fontStyle="italic" />
          <RuleSet>
            <Span color="Comment" begin="//" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String" ruleSet="StringContent" multiline="true">
              <Begin>"</Begin>
              <End>"</End>
            </Span>
            <Rule color="Directive">\#[A-Za-z_]+</Rule>
            <Keywords color="Keyword">
              <Word>if</Word><Word>else</Word><Word>repeat</Word><Word>foreach</Word><Word>menu</Word>
              <Word>case</Word><Word>var</Word><Word>action</Word><Word>with</Word><Word>as</Word>
              <Word>true</Word><Word>false</Word><Word>contains</Word><Word>beginsWith</Word>
              <Word>endsWith</Word><Word>hasValue</Word><Word>between</Word><Word>and</Word>
            </Keywords>
            <Keywords color="Builtin">
              <Word>ShortcutInput</Word><Word>Clipboard</Word><Word>CurrentDate</Word><Word>Ask</Word>
              <Word>DeviceDetails</Word><Word>RepeatItem</Word><Word>RepeatIndex</Word>
            </Keywords>
            <Rule color="Form">\b(text|fields|wrap|data|date|output|var|raw|global)(?=\()</Rule>
            <Rule color="Action">\b[A-Z][A-Za-z0-9_]*(?=\s*\()</Rule>
            <Rule color="Number">-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b</Rule>
          </RuleSet>
          <RuleSet name="StringContent">
            <Span begin="\\" end="." />
            <Span color="Interpolation" begin="\{" end="\}" />
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static readonly Dictionary<string, (string Light, string Dark)> Colors = new()
    {
        ["Comment"] = ("#3C8D40", "#6A9955"),
        ["Directive"] = ("#AF00DB", "#C586C0"),
        ["String"] = ("#A31515", "#CE9178"),
        ["Interpolation"] = ("#0070C1", "#4FC1FF"),
        ["Keyword"] = ("#0000FF", "#569CD6"),
        ["Number"] = ("#098658", "#B5CEA8"),
        ["Action"] = ("#795E26", "#DCDCAA"),
        ["Builtin"] = ("#267F99", "#4EC9B0"),
        ["Form"] = ("#001080", "#9CDCFE"),
    };

    private static readonly Dictionary<bool, IHighlightingDefinition> Cache = new();

    public static IHighlightingDefinition Get(bool dark)
    {
        if (Cache.TryGetValue(dark, out var cached)) return cached;
        var xshd = Colors.Aggregate(Xshd, (text, c) => text.Replace("{" + c.Key + "}", dark ? c.Value.Dark : c.Value.Light));
        using var reader = XmlReader.Create(new StringReader(xshd));
        return Cache[dark] = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
