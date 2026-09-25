using ICSharpCode.AvalonEdit.Document;
using ShortcutForge.App.Editor;

namespace ShortcutForge.App.Tests;

public class EditorSupportTests
{
    [Fact]
    public void Folds_multiline_blocks_but_not_braces_in_strings_or_comments()
    {
        var doc = new TextDocument("""
            if x hasValue {
                ShowResult("{x.get("Name")} \} \{")   // } {
                repeat 2 {
                    Vibrate()
                }
            }
            Alert("egy sor { }")
            """);

        var foldings = DslEditorSupport.ComputeFoldings(doc).ToList();

        Assert.Equal(2, foldings.Count);
        Assert.Equal(1, doc.GetLineByOffset(foldings[0].StartOffset).LineNumber);
        Assert.Equal(6, doc.GetLineByOffset(foldings[0].EndOffset - 1).LineNumber);
        Assert.Equal(3, doc.GetLineByOffset(foldings[1].StartOffset).LineNumber);
    }
}
