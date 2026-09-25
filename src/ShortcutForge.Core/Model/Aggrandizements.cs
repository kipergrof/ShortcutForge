using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Model;

/// <summary>
/// "Aggrandizements" are what the Shortcuts app shows when you tap a variable: get it as a
/// type (coercion), get one of its properties, or get a dictionary value by key.
/// They live in the variable reference's Extra["Aggrandizements"] array.
/// </summary>
public static class Aggrandizements
{
    public const string Key = "Aggrandizements";
    public const string CoercionType = "WFCoercionVariableAggrandizement";
    public const string PropertyType = "WFPropertyVariableAggrandizement";
    public const string DictionaryValueType = "WFDictionaryValueVariableAggrandizement";

    /// <summary>A content type the variable can be read as.</summary>
    public sealed record ContentType(string Name, string ItemClass, string Hu, string En, IReadOnlyList<string> Properties)
    {
        public string Label => L.T(Hu, En);
        public override string ToString() => Label;
    }

    /// <summary>Common types with the property names the Shortcuts app offers for them.</summary>
    public static IReadOnlyList<ContentType> Types { get; } =
    [
        new("Text", "WFStringContentItem", "Szöveg", "Text", ["Name"]),
        new("Number", "WFNumberContentItem", "Szám", "Number", ["Name"]),
        new("Boolean", "WFBooleanContentItem", "Logikai", "Boolean", ["Name"]),
        new("Dictionary", "WFDictionaryContentItem", "Szótár", "Dictionary", ["Name"]),
        new("Date", "WFDateContentItem", "Dátum", "Date", ["Name"]),
        new("URL", "WFURLContentItem", "URL", "URL", ["Name", "Host", "Path", "Query", "Scheme", "Port", "Fragment"]),
        new("RichText", "WFRichTextContentItem", "Formázott szöveg", "Rich Text", ["Name"]),
        new("File", "WFGenericFileContentItem", "Fájl", "File",
            ["Name", "File Size", "File Extension", "Creation Date", "Last Modified Date", "File Path"]),
        new("Image", "WFImageContentItem", "Kép", "Image",
            ["Name", "Width", "Height", "Date Taken", "Camera Make", "Camera Model", "Orientation", "Location", "Duration", "Frame Count", "Is a Screenshot", "Album", "File Size", "File Extension"]),
        new("PDF", "WFPDFContentItem", "PDF", "PDF", ["Name", "Number of Pages", "File Size"]),
        new("Media", "WFAVAssetContentItem", "Média", "Media", ["Name", "Duration", "Width", "Height", "File Size"]),
        new("Contact", "WFContactContentItem", "Kontakt", "Contact",
            ["Name", "First Name", "Last Name", "Nickname", "Company", "Job Title", "Phone Numbers", "Email Addresses", "Street Addresses", "Birthday", "Notes", "Image"]),
        new("Location", "WFLocationContentItem", "Hely", "Location",
            ["Name", "Latitude", "Longitude", "Altitude", "Street", "City", "State", "ZIP Code", "Country", "Phone Number", "URL"]),
        new("Email", "WFEmailAddressContentItem", "E-mail cím", "Email Address", ["Name"]),
        new("Phone", "WFPhoneNumberContentItem", "Telefonszám", "Phone Number", ["Name"]),
        new("Article", "WFArticleContentItem", "Cikk", "Article", ["Name", "Title", "Author", "Published Date", "Body", "URL", "Excerpt", "Main Image URL", "Number of Words"]),
        new("SafariWebPage", "WFSafariWebPageContentItem", "Safari weboldal", "Safari Web Page", ["Name", "Page Contents", "Page Selection", "Page URL"]),
        new("App", "WFAppContentItem", "App", "App", ["Name", "Bundle Identifier", "Version"]),
    ];

    public static ContentType? TypeByName(string name) =>
        Types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ContentType? TypeByClass(string itemClass) => Types.FirstOrDefault(t => t.ItemClass == itemClass);

    public static DictValue Coercion(string itemClass) => new([
        new("Type", new StringValue(CoercionType)),
        new("CoercionItemClass", new StringValue(itemClass)),
    ]);

    public static DictValue Property(string propertyName) => new([
        new("Type", new StringValue(PropertyType)),
        new("PropertyName", new StringValue(propertyName)),
    ]);

    public static DictValue DictionaryValue(string key) => new([
        new("Type", new StringValue(DictionaryValueType)),
        new("DictionaryKey", new StringValue(key)),
    ]);

    /// <summary>The aggrandizement list of a variable (empty if none).</summary>
    public static IReadOnlyList<ParamValue> Of(VariableRef variable) =>
        variable.Extra?[Key] is ArrayValue arr ? arr.Items : [];

    /// <summary>Returns the variable with <paramref name="item"/> appended to its aggrandizements.</summary>
    public static VariableRef Append(VariableRef variable, DictValue item)
    {
        var items = Of(variable).Append(item).ToList();
        return WithList(variable, items);
    }

    public static VariableRef WithList(VariableRef variable, IReadOnlyList<ParamValue> items)
    {
        var others = variable.Extra?.Entries.Where(e => e.Key != Key).ToList() ?? [];
        if (items.Count > 0) others.Add(new(Key, new ArrayValue(items)));
        return variable with { Extra = others.Count > 0 ? new DictValue(others) : null };
    }

    /// <summary>Decomposes a variable's aggrandizements into the simple (type, property, key) form, if possible.</summary>
    public static bool TryDescribe(VariableRef variable, out string? itemClass, out string? property, out string? dictionaryKey)
    {
        itemClass = property = dictionaryKey = null;
        if (variable.Extra is { } extra && extra.Entries.Any(e => e.Key != Key)) return false;
        foreach (var item in Of(variable))
        {
            if (item is not DictValue d) return false;
            switch (d["Type"])
            {
                case StringValue { Value: CoercionType } when d.Entries.Count == 2 && d["CoercionItemClass"] is StringValue c && itemClass is null && property is null && dictionaryKey is null:
                    itemClass = c.Value;
                    break;
                case StringValue { Value: PropertyType } when d.Entries.Count == 2 && d["PropertyName"] is StringValue p && property is null && dictionaryKey is null:
                    property = p.Value;
                    break;
                case StringValue { Value: DictionaryValueType } when d.Entries.Count == 2 && d["DictionaryKey"] is StringValue k && property is null && dictionaryKey is null:
                    dictionaryKey = k.Value;
                    break;
                default:
                    return false;
            }
        }
        return true;
    }
}
