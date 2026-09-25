using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Model;

public sealed class Shortcut
{
    public const string DefaultClientVersion = "2607.0.2";

    /// <summary>Shortcut name. Not stored in the plist; derived from the file name.</summary>
    public string Name { get; set; } = L.T("Új parancs", "New shortcut");

    public ShortcutIcon Icon { get; set; } = ShortcutIcon.Default;

    /// <summary>Accepted input content classes (WFWorkflowInputContentItemClasses).</summary>
    public List<string> InputClasses { get; set; } = [];

    /// <summary>Where the shortcut appears (WFWorkflowTypes), e.g. NCWidget, WatchKit, ActionExtension.</summary>
    public List<string> Types { get; set; } = [];

    public string ClientVersion { get; set; } = DefaultClientVersion;

    public List<ActionInstance> Actions { get; set; } = [];

    /// <summary>Any other top-level plist keys, kept verbatim (import questions, output classes, ...).</summary>
    public Dictionary<string, ParamValue> Extra { get; set; } = new();
}

/// <summary>Icon color (packed RGBA integer) and SF glyph number.</summary>
public sealed record ShortcutIcon(long StartColor, long GlyphNumber)
{
    public static readonly ShortcutIcon Default = new(463140863, 59511);

    /// <summary>Colors offered by the Shortcuts app, with their packed RGBA values.</summary>
    public static readonly IReadOnlyList<(string Name, long Value)> Colors =
    [
        ("red", 4282601983),        // #FF4351
        ("darkorange", 4251333119), // #FD6631
        ("orange", 4271458815),     // #FE9949
        ("gold", 4274264319),       // #FEC418
        ("yellow", 4292093695),     // #FFD426
        ("green", 431817727),       // #19BD03
        ("teal", 1440408063),       // #55DAE1
        ("blue", 463140863),        // #1B9AF7
        ("darkblue", 946986751),    // #3871DE
        ("violet", 2071128575),     // #7B72E9
        ("purple", 3679049983),     // #DB49D8
        ("pink", 3980825855),       // #ED4694
        ("gray", 2846468607),       // #A9A9A9
        ("slate", 1685231615),      // #647297
    ];

    public static long? ColorByName(string name) =>
        Colors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is var c && c.Name is not null
            ? c.Value
            : null;

    public static string? NameOfColor(long value) => Colors.FirstOrDefault(c => c.Value == value).Name;

    public (byte R, byte G, byte B) Rgb =>
        ((byte)((StartColor >> 24) & 0xFF), (byte)((StartColor >> 16) & 0xFF), (byte)((StartColor >> 8) & 0xFF));
}
