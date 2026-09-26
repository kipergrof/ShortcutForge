using System.Text;
using ShortcutForge.Core.Import;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Dsl;

namespace ShortcutForge.Editor;

/// <summary>Opening and saving shortcut files (the same formats as the Windows app).</summary>
public static class ShortcutDocument
{
    public const string ProjectExtension = ".sfdsl";

    /// <summary>Extensions the Open dialog offers.</summary>
    public static readonly string[] OpenExtensions = [".sfdsl", ".shortcut", ".plist", ".wflow"];

    public static bool IsProject(string path) =>
        Path.GetExtension(path).Equals(ProjectExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens a .sfdsl text file, or any shortcut file (signed / unsigned .shortcut, binary or XML plist).</summary>
    public static Shortcut Open(string path) => Open(File.ReadAllBytes(path), Path.GetFileName(path));

    /// <summary>Opens file content; <paramref name="fileName"/> decides the format and gives the default name.</summary>
    public static Shortcut Open(byte[] data, string fileName)
    {
        if (IsProject(fileName))
            return DslParser.Parse(Encoding.UTF8.GetString(data).TrimStart('﻿'));
        return ShortcutFileReader.Read(data, Path.GetFileNameWithoutExtension(fileName));
    }

    public static string ProjectText(Shortcut shortcut) => DslPrinter.Print(shortcut);

    public static void SaveProject(Shortcut shortcut, string path) =>
        File.WriteAllText(path, ProjectText(shortcut), new UTF8Encoding(false));

    public static byte[] UnsignedBytes(Shortcut shortcut) => PlistSerializer.WriteBinary(shortcut);

    /// <summary>A file name without characters that are invalid on any common file system.</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Shortcut" : cleaned;
    }

    public static string ExampleSource => Core.Localization.L.IsEnglish ? ExampleSourceEn : ExampleSourceHu;

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
