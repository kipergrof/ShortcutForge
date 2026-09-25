using System.IO;
using System.Text;
using ShortcutForge.Core.Import;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Dsl;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Services;

/// <summary>Opening and saving shortcut files.</summary>
public static class ShortcutDocument
{
    public const string ProjectExtension = ".sfdsl";

    public static string OpenFilter =>
        L.T("Minden támogatott|*.sfdsl;*.shortcut;*.plist;*.wflow|ShortcutForge szöveg (*.sfdsl)|*.sfdsl|", "All supported|*.sfdsl;*.shortcut;*.plist;*.wflow|ShortcutForge text (*.sfdsl)|*.sfdsl|") +
        L.T("Shortcut fájl (*.shortcut)|*.shortcut|Plist (*.plist;*.wflow)|*.plist;*.wflow|Minden fájl|*.*", "Shortcut file (*.shortcut)|*.shortcut|Plist (*.plist;*.wflow)|*.plist;*.wflow|All files|*.*");

    public static string SaveFilter => L.T("ShortcutForge szöveg (*.sfdsl)|*.sfdsl", "ShortcutForge text (*.sfdsl)|*.sfdsl");

    public static string ExportFilter => L.T("Shortcut fájl (*.shortcut)|*.shortcut|XML plist (*.plist)|*.plist", "Shortcut file (*.shortcut)|*.shortcut|XML plist (*.plist)|*.plist");

    public static Shortcut Open(string path)
    {
        if (Path.GetExtension(path).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = DslParser.Parse(File.ReadAllText(path, Encoding.UTF8));
            return shortcut;
        }
        return ShortcutFileReader.ReadFile(path);
    }

    public static void SaveProject(Shortcut shortcut, string path) =>
        File.WriteAllText(path, DslPrinter.Print(shortcut), new UTF8Encoding(false));

    public static byte[] UnsignedBytes(Shortcut shortcut) => PlistSerializer.WriteBinary(shortcut);

    public static string ExampleSource => L.IsEnglish ? ExampleSourceEn : ExampleSourceHu;

    private const string ExampleSourceEn = """
        #name "My first shortcut"
        #icon color=blue glyph=59511

        // Greet the user by name
        name = AskForInput(prompt: "What's your name?", type: "Text")
        greeting = Text("Hi, {name}! Nice to see you.")
        if name hasValue {
            Notification(greeting, title: "ShortcutForge")
        } else {
            Alert("You didn't enter a name.", title: "Oops")
        }
        """;

    private const string ExampleSourceHu = """
        #name "Első parancsom"
        #icon color=blue glyph=59511

        // Üdvözlés a bemenettel vagy egy kérdéssel
        name = AskForInput(prompt: "Hogy hívnak?", type: "Text")
        greeting = Text("Szia, {name}! Jó, hogy itt vagy.")
        if name hasValue {
            Notification(greeting, title: "ShortcutForge")
        } else {
            Alert("Nem adtál meg nevet.", title: "Hoppá")
        }
        """;
}
