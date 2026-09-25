namespace ShortcutForge.Core.Model;

/// <summary>The kind of a variable reference ("Type" key of a WFTextTokenAttachment).</summary>
public static class VariableKinds
{
    /// <summary>Magic variable: output of a previous action (OutputUUID + OutputName).</summary>
    public const string ActionOutput = "ActionOutput";

    /// <summary>Named variable set with Set Variable / Add to Variable, or Repeat Item/Index.</summary>
    public const string Variable = "Variable";

    /// <summary>Shortcut Input.</summary>
    public const string ShortcutInput = "ExtensionInput";

    public const string CurrentDate = "CurrentDate";
    public const string Clipboard = "Clipboard";
    public const string Ask = "Ask";
    public const string DeviceDetails = "DeviceDetails";
}

/// <summary>
/// A reference to a variable. <see cref="Extra"/> keeps any additional keys
/// (e.g. Aggrandizements for property access / type coercion) so nothing is lost.
/// </summary>
public sealed record VariableRef(
    string Kind,
    string? OutputUuid = null,
    string? OutputName = null,
    string? VariableName = null,
    DictValue? Extra = null)
{
    public static VariableRef Output(string uuid, string name) => new(VariableKinds.ActionOutput, uuid, name);
    public static VariableRef Named(string name) => new(VariableKinds.Variable, VariableName: name);
    public static VariableRef Global(string kind) => new(kind);

    public const string RepeatItem = "Repeat Item";
    public const string RepeatIndex = "Repeat Index";

    public bool Equals(VariableRef? other) =>
        other is not null && Kind == other.Kind && OutputUuid == other.OutputUuid &&
        OutputName == other.OutputName && VariableName == other.VariableName &&
        Equals(Extra, other.Extra);

    public override int GetHashCode() => HashCode.Combine(Kind, OutputUuid, VariableName);
}
