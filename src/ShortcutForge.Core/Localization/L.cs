namespace ShortcutForge.Core.Localization;

/// <summary>
/// Minimal two-language localization (Hungarian / English). Strings are written as pairs at the
/// call site: <c>L.T("Mentés", "Save")</c>. Changing <see cref="Language"/> raises <see cref="Changed"/>.
/// </summary>
public static class L
{
    public const string Hungarian = "hu";
    public const string English = "en";

    private static string _language = Hungarian;

    public static string Language
    {
        get => _language;
        set
        {
            var normalized = value == English ? English : Hungarian;
            if (normalized == _language) return;
            _language = normalized;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool IsEnglish => _language == English;

    public static event EventHandler? Changed;

    public static string T(string hu, string en) => IsEnglish ? en : hu;

    public static string F(string hu, string en, params object?[] args) => string.Format(T(hu, en), args);
}
