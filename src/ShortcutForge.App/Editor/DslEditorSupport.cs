using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Dsl;
using ShortcutForge.Core.Localization;
using ShortcutForge.App.Services;

namespace ShortcutForge.App.Editor;

/// <summary>Syntax highlighting, error marking and completion for the DSL editor.</summary>
public static class DslEditorSupport
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

    private static readonly Dictionary<bool, IHighlightingDefinition> HighlightingCache = new();

    private static IHighlightingDefinition Highlighting(bool dark)
    {
        if (HighlightingCache.TryGetValue(dark, out var cached)) return cached;
        var xshd = Colors.Aggregate(Xshd, (text, c) => text.Replace("{" + c.Key + "}", dark ? c.Value.Dark : c.Value.Light));
        using var reader = XmlReader.Create(new StringReader(xshd));
        return HighlightingCache[dark] = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    public static void Attach(TextEditor editor, Func<IEnumerable<string>> variableNames)
    {
        // Colors follow the app theme (light / dark).
        editor.SyntaxHighlighting = Highlighting(ThemeManager.IsDark);
        editor.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "EditorBg");
        editor.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "EditorFg");
        editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "LineNumberFg");
        var weak = new WeakReference<TextEditor>(editor);
        EventHandler? onTheme = null;
        onTheme = (_, _) =>
        {
            if (weak.TryGetTarget(out var e)) e.SyntaxHighlighting = Highlighting(ThemeManager.IsDark);
            else ThemeManager.ThemeChanged -= onTheme;
        };
        ThemeManager.ThemeChanged += onTheme;
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        editor.TextArea.TextView.BackgroundRenderers.Add(new ErrorRenderer(editor));

        CompletionWindow? window = null;
        editor.TextArea.TextEntered += (_, e) =>
        {
            if (window is not null || e.Text.Length != 1) return;
            var c = e.Text[0];
            if (!Lexer.IsIdentPart(c)) return;
            var (start, word) = CurrentWord(editor.TextArea);
            if (word.Length < 2 || IsInsideStringOrComment(editor, start)) return;
            window = ShowCompletion(editor.TextArea, start, word, variableNames());
            if (window is not null) window.Closed += (_, _) => window = null;
        };
        editor.TextArea.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Space &&
                System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                e.Handled = true;
                var (start, word) = CurrentWord(editor.TextArea);
                window?.Close();
                window = ShowCompletion(editor.TextArea, start, word, variableNames(), force: true);
                if (window is not null) window.Closed += (_, _) => window = null;
            }
        };
        editor.TextArea.TextEntering += (_, e) =>
        {
            if (window is not null && e.Text.Length > 0 && !Lexer.IsIdentPart(e.Text[0]))
                window.CompletionList.RequestInsertion(e);
        };
    }

    private static (int Start, string Word) CurrentWord(TextArea area)
    {
        var doc = area.Document;
        var end = area.Caret.Offset;
        var start = end;
        while (start > 0 && Lexer.IsIdentPart(doc.GetCharAt(start - 1))) start--;
        return (start, doc.GetText(start, end - start));
    }

    private static bool IsInsideStringOrComment(TextEditor editor, int offset)
    {
        var line = editor.Document.GetLineByOffset(offset);
        var before = editor.Document.GetText(line.Offset, offset - line.Offset);
        if (before.Contains("//")) return true;
        var quotes = before.Count(ch => ch == '"') - CountEscapedQuotes(before);
        return quotes % 2 == 1 && before.LastIndexOf('{') < before.LastIndexOf('"');
    }

    private static int CountEscapedQuotes(string s)
    {
        var n = 0;
        for (var i = 1; i < s.Length; i++) if (s[i] == '"' && s[i - 1] == '\\') n++;
        return n;
    }

    private static CompletionWindow? ShowCompletion(TextArea area, int start, string word, IEnumerable<string> variables, bool force = false)
    {
        var items = new List<ICompletionData>();
        foreach (var a in ActionCatalog.Default.Actions)
            items.Add(new CompletionItem(a.Dsl, a.Dsl + "(", $"{a.Name} · {a.DisplayCategory}\n{a.DisplayDescription}\n" +
                string.Join(", ", a.Params.Select(p => $"{p.Name}: {p.Type}")), 2));
        foreach (var v in variables.Distinct())
            items.Add(new CompletionItem(v, v, L.T("Változó", "Variable"), 1));
        foreach (var k in new[] { "if", "else", "repeat", "foreach", "menu", "case", "var", "action", "with", "true", "false",
                                  "contains", "beginsWith", "endsWith", "hasValue", "between" })
            items.Add(new CompletionItem(k, k, L.T("Kulcsszó", "Keyword"), 0));

        var matching = items.Where(i => force || i.Text.StartsWith(word, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matching.Count == 0 || (matching.Count == 1 && matching[0].Text == word)) return null;

        var window = new CompletionWindow(area) { StartOffset = start, CloseWhenCaretAtBeginning = true };
        foreach (var i in matching.OrderByDescending(i => ((CompletionItem)i).Priority).ThenBy(i => i.Text))
            window.CompletionList.CompletionData.Add(i);
        window.Show();
        window.CompletionList.SelectItem(word);
        return window;
    }

    private sealed class CompletionItem(string text, string insert, string description, int priority) : ICompletionData
    {
        public ImageSource? Image => null;
        public string Text { get; } = text;
        public object Content => Text;
        public object Description { get; } = description;
        public double Priority { get; } = priority;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, insert);
    }

    /// <summary>Underlines the error location with a red squiggle-like bar.</summary>
    public sealed class ErrorRenderer(TextEditor editor) : IBackgroundRenderer
    {
        public int Offset { get; set; } = -1;
        public int Length { get; set; }

        public KnownLayer Layer => KnownLayer.Selection;

        private static readonly Brush Fill = Freeze(new SolidColorBrush(Color.FromArgb(60, 255, 0, 0)));
        private static readonly Pen Underline = FreezePen(new Pen(Brushes.Red, 2));

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (Offset < 0 || Offset > editor.Document.TextLength) return;
            var length = Math.Max(1, Math.Min(Length, editor.Document.TextLength - Offset));
            if (Offset + length > editor.Document.TextLength) return;
            var segment = new TextSegment { StartOffset = Offset, Length = length };
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                drawingContext.DrawRectangle(Fill, null, rect);
                drawingContext.DrawLine(Underline, rect.BottomLeft, rect.BottomRight);
            }
        }

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
        private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
    }

    /// <summary>Makes multi-line { … } blocks collapsible (braces inside strings and comments are ignored).</summary>
    public static void EnableFolding(TextEditor editor)
    {
        var manager = ICSharpCode.AvalonEdit.Folding.FoldingManager.Install(editor.TextArea);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            manager.UpdateFoldings(ComputeFoldings(editor.Document), -1);
        };
        editor.Document.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
        editor.DocumentChanged += (_, _) =>
        {
            editor.Document.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
            timer.Start();
        };
        timer.Start();
    }

    public static IEnumerable<ICSharpCode.AvalonEdit.Folding.NewFolding> ComputeFoldings(TextDocument document)
    {
        var text = document.Text;
        var result = new List<ICSharpCode.AvalonEdit.Folding.NewFolding>();
        var stack = new Stack<int>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
                continue;
            }
            if (c == '"')
            {
                // Skip the string literal, including nested {…} interpolations with their own strings.
                var depth = 0;
                for (i++; i < text.Length; i++)
                {
                    if (text[i] == '\\') { i++; continue; }
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}' && depth > 0) depth--;
                    else if (text[i] == '"' && depth == 0) break;
                }
                continue;
            }
            if (c == '{') stack.Push(i);
            else if (c == '}' && stack.Count > 0)
            {
                var start = stack.Pop();
                if (document.GetLineByOffset(start).LineNumber != document.GetLineByOffset(i).LineNumber)
                    result.Add(new ICSharpCode.AvalonEdit.Folding.NewFolding(start, i + 1) { Name = "{ … }" });
            }
        }
        result.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return result;
    }

    public static void ShowError(TextEditor editor, int offset, int length)
    {
        var renderer = editor.TextArea.TextView.BackgroundRenderers.OfType<ErrorRenderer>().FirstOrDefault();
        if (renderer is null) return;
        renderer.Offset = offset;
        renderer.Length = length;
        editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }
}
