using System.Globalization;
using System.Windows.Data;
using ShortcutForge.Core.Catalog;

namespace ShortcutForge.App.Services;

/// <summary>Color and icon (Segoe Fluent Icons glyph) of an action, based on its catalog category.</summary>
public static class CategoryStyle
{
    private static readonly Dictionary<string, (string Color, string Glyph)> Styles = new()
    {
        ["Vezérlés"] = ("#8E8E93", ""),
        ["Változók"] = ("#FF9500", ""),
        ["Szöveg"] = ("#E6B800", ""),
        ["Számok és matek"] = ("#5AC8FA", ""),
        ["Listák és szótárak"] = ("#FF9F0A", ""),
        ["Dátum és idő"] = ("#FF3B30", ""),
        ["Web"] = ("#007AFF", ""),
        ["Párbeszédek és értesítések"] = ("#FF2D55", ""),
        ["Fájlok és dokumentumok"] = ("#34AADC", ""),
        ["Média és fotók"] = ("#AF52DE", ""),
        ["Eszköz"] = ("#4CD964", ""),
        ["Appok és kommunikáció"] = ("#34C759", ""),
    };

    private const string UnknownColor = "#7D6BC4";
    private const string UnknownGlyph = "";

    public static string ColorOf(ActionDefinition? definition) =>
        definition is null ? UnknownColor
        : definition.Control != ControlFlowKind.None ? "#8E8E93"
        : Styles.TryGetValue(definition.Category, out var s) ? s.Color : "#636366";

    public static string GlyphOf(ActionDefinition? definition) => definition?.Control switch
    {
        null => UnknownGlyph,
        ControlFlowKind.If => "",
        ControlFlowKind.Repeat or ControlFlowKind.RepeatEach => "",
        ControlFlowKind.Menu => "",
        _ => Styles.TryGetValue(definition.Category, out var s) ? s.Glyph : UnknownGlyph,
    };
}

/// <summary>ActionDefinition → brush (parameter "brush") or glyph (default), for the library list.</summary>
public sealed class CategoryStyleConverter : IValueConverter
{
    private static readonly Dictionary<string, System.Windows.Media.Brush> Brushes = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var definition = value as ActionDefinition;
        if (parameter as string != "brush") return CategoryStyle.GlyphOf(definition);
        var color = CategoryStyle.ColorOf(definition);
        if (!Brushes.TryGetValue(color, out var brush))
        {
            brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            Brushes[color] = brush;
        }
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
