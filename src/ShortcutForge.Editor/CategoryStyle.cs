using ShortcutForge.Core.Catalog;

namespace ShortcutForge.Editor;

/// <summary>
/// Colour of an action based on its catalog category (same palette as the Windows app), plus a
/// short text badge used instead of an icon font, which is not available on every platform.
/// </summary>
public static class CategoryStyle
{
    private static readonly Dictionary<string, string> Colors = new()
    {
        ["Vezérlés"] = "#8E8E93",
        ["Változók"] = "#FF9500",
        ["Szöveg"] = "#E6B800",
        ["Számok és matek"] = "#5AC8FA",
        ["Listák és szótárak"] = "#FF9F0A",
        ["Dátum és idő"] = "#FF3B30",
        ["Web"] = "#007AFF",
        ["Párbeszédek és értesítések"] = "#FF2D55",
        ["Fájlok és dokumentumok"] = "#34AADC",
        ["Média és fotók"] = "#AF52DE",
        ["Eszköz"] = "#4CD964",
        ["Appok és kommunikáció"] = "#34C759",
    };

    public const string UnknownColor = "#7D6BC4";

    public static string ColorOf(ActionDefinition? definition) =>
        definition is null ? UnknownColor
        : definition.Control != ControlFlowKind.None ? "#8E8E93"
        : Colors.TryGetValue(definition.Category, out var c) ? c : "#636366";

    /// <summary>One or two characters shown on the coloured square of a card or library row.</summary>
    public static string BadgeOf(ActionDefinition? definition, string? title = null) => definition?.Control switch
    {
        null => "?",
        ControlFlowKind.If => "if",
        ControlFlowKind.Repeat or ControlFlowKind.RepeatEach => "↻",
        ControlFlowKind.Menu => "≡",
        _ => FirstLetter(title ?? definition.Name),
    };

    private static string FirstLetter(string text)
    {
        foreach (var c in text)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "•";
    }
}
