using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ShortcutForge.Desktop;

public static class Converters
{
    private static readonly Dictionary<string, IBrush> Brushes = new();

    /// <summary>"#RRGGBB" → brush.</summary>
    public static readonly IValueConverter HexToBrush = new FuncValueConverter<string?, IBrush?>(hex =>
    {
        if (string.IsNullOrEmpty(hex) || !Color.TryParse(hex, out var color)) return null;
        if (!Brushes.TryGetValue(hex, out var brush)) Brushes[hex] = brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);
        return brush;
    });

    /// <summary>Collapsed flag → chevron of the collapse button.</summary>
    public static readonly IValueConverter CollapseChevron = new FuncValueConverter<bool, string>(collapsed => collapsed ? "▸" : "▾");
}
