using System.Globalization;
using System.Text;

namespace ShortcutForge.App.ViewModels;

/// <summary>One entry of the command palette (Ctrl+K).</summary>
public sealed record PaletteItem(string Title, string Category, string Glyph, Action Execute, string? Keywords = null, string? Shortcut = null)
{
    public override string ToString() => Title;
}

/// <summary>Fuzzy, accent- and case-insensitive matching for the command palette.</summary>
public static class PaletteMatcher
{
    /// <summary>
    /// Score of <paramref name="query"/> against <paramref name="text"/>; null if it does not match.
    /// Every query character must appear in order. Consecutive runs and word starts score higher.
    /// </summary>
    public static int? Score(string query, string text)
    {
        var q = Fold(query.Trim());
        if (q.Length == 0) return 0;
        var t = Fold(text);

        var contains = t.IndexOf(q, StringComparison.Ordinal);
        if (contains >= 0) return 1000 - contains + (contains == 0 || !char.IsLetterOrDigit(t[contains - 1]) ? 200 : 0);

        var score = 0;
        var ti = 0;
        var run = 0;
        foreach (var c in q)
        {
            if (c == ' ') continue;
            var found = t.IndexOf(c, ti);
            if (found < 0) return null;
            var wordStart = found == 0 || !char.IsLetterOrDigit(t[found - 1]);
            run = found == ti ? run + 1 : 0;
            score += 10 + run * 5 + (wordStart ? 15 : 0) - Math.Min(found - ti, 10);
            ti = found + 1;
        }
        // Letters scattered far apart are not a useful match.
        return score >= q.Replace(" ", "").Length * 14 ? score : null;
    }

    /// <summary>Items matching <paramref name="query"/>, best first.</summary>
    public static IReadOnlyList<PaletteItem> Filter(IEnumerable<PaletteItem> items, string query, int max = 60)
    {
        if (string.IsNullOrWhiteSpace(query)) return items.Take(max).ToList();
        return items
            .Select(item => (item, score: Best(query, item)))
            .Where(x => x.score is not null)
            .OrderByDescending(x => x.score)
            .Take(max)
            .Select(x => x.item)
            .ToList();
    }

    private static int? Best(string query, PaletteItem item)
    {
        // Fuzzy matching only on the title; keywords (DSL name, descriptions) must contain the
        // query as a whole, otherwise long descriptions would match almost anything.
        var title = Score(query, item.Title);
        int? keywords = item.Keywords is not null && Fold(item.Keywords).Contains(Fold(query.Trim()), StringComparison.Ordinal)
            ? 500
            : null;
        return title is null ? keywords : keywords is null ? title : Math.Max(title.Value, keywords.Value);
    }

    /// <summary>Lower case without accents ("Exportálás" → "exportalas").</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
