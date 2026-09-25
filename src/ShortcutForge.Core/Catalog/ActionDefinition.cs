using System.Text;
using System.Text.Json.Serialization;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Catalog;

/// <summary>How a parameter is edited and serialized.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParamKind>))]
public enum ParamKind
{
    /// <summary>Text that may contain variables (WFTextTokenString).</summary>
    Text,
    /// <summary>Plain string without variables.</summary>
    String,
    /// <summary>A single variable (WFTextTokenAttachment), e.g. an action's input.</summary>
    Variable,
    Number,
    Integer,
    Bool,
    /// <summary>One of <see cref="ParamDefinition.Options"/>.</summary>
    Enum,
    /// <summary>Dictionary built with key/value rows (WFDictionaryFieldValue).</summary>
    Dictionary,
    /// <summary>List of text items (List action).</summary>
    List,
    /// <summary>Anything else; edited as a raw value.</summary>
    Raw,
}

[JsonConverter(typeof(JsonStringEnumConverter<ControlFlowKind>))]
public enum ControlFlowKind
{
    None,
    If,
    Repeat,
    RepeatEach,
    Menu,
}

public sealed class ParamDefinition
{
    /// <summary>Plist key, e.g. WFAlertActionTitle.</summary>
    public required string Key { get; init; }

    /// <summary>Argument name in the DSL, e.g. "title".</summary>
    public required string Name { get; init; }

    public string? Label { get; init; }

    public ParamKind Type { get; init; } = ParamKind.Text;

    public List<string>? Options { get; init; }

    public string? Description { get; init; }

    public string? LabelEn { get; init; }

    /// <summary>Label in the current UI language; English falls back to the (English) DSL name.</summary>
    [JsonIgnore]
    public string DisplayLabel => L.IsEnglish ? LabelEn ?? Humanize(Name) : Label ?? Name;

    /// <summary>"caseSensitive" → "Case sensitive".</summary>
    public static string Humanize(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (sb.Length == 0) sb.Append(char.ToUpperInvariant(c));
            else if (char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

public sealed class ActionDefinition
{
    /// <summary>WFWorkflowActionIdentifier, e.g. is.workflow.actions.alert.</summary>
    public required string Id { get; init; }

    /// <summary>Display name as in the Shortcuts app.</summary>
    public required string Name { get; init; }

    /// <summary>Function name in the DSL, e.g. "Alert".</summary>
    public required string Dsl { get; init; }

    public string Category { get; set; } = "Egyéb";

    public string? Description { get; init; }

    public string? DescriptionEn { get; init; }

    [JsonIgnore]
    public string? DisplayDescription => L.IsEnglish ? DescriptionEn : Description;

    /// <summary>Category name in the current UI language.</summary>
    [JsonIgnore]
    public string DisplayCategory => CategoryName(Category);

    private static readonly Dictionary<string, string> CategoryEn = new()
    {
        ["Vezérlés"] = "Scripting",
        ["Változók"] = "Variables",
        ["Szöveg"] = "Text",
        ["Számok és matek"] = "Numbers & Math",
        ["Listák és szótárak"] = "Lists & Dictionaries",
        ["Dátum és idő"] = "Date & Time",
        ["Web"] = "Web",
        ["Párbeszédek és értesítések"] = "Dialogs & Notifications",
        ["Fájlok és dokumentumok"] = "Files & Documents",
        ["Média és fotók"] = "Media & Photos",
        ["Eszköz"] = "Device",
        ["Appok és kommunikáció"] = "Apps & Communication",
        ["Egyéb"] = "Other",
    };

    public static string CategoryName(string category) =>
        L.IsEnglish && CategoryEn.TryGetValue(category, out var en) ? en : category;

    /// <summary>Default name of the magic variable produced by this action; null if no output.</summary>
    public string? Output { get; init; }

    public ControlFlowKind Control { get; init; } = ControlFlowKind.None;

    public List<ParamDefinition> Params { get; init; } = [];

    /// <summary>Only on macOS.</summary>
    public bool MacOnly { get; init; }

    public ParamDefinition? FindParam(string nameOrKey) =>
        Params.FirstOrDefault(p => p.Name == nameOrKey) ??
        Params.FirstOrDefault(p => p.Key == nameOrKey) ??
        Params.FirstOrDefault(p => string.Equals(p.Name, nameOrKey, StringComparison.OrdinalIgnoreCase));

    public ParamDefinition? ParamByKey(string key) => Params.FirstOrDefault(p => p.Key == key);

    public override string ToString() => Name;
}
