namespace ShortcutForge.Core.Model;

/// <summary>
/// A shortcut action parameter value. Mirrors the plist structure used by the Shortcuts app
/// closely enough that any value read from a file can be written back without loss.
/// </summary>
public abstract record ParamValue;

public sealed record StringValue(string Value) : ParamValue;

public sealed record IntegerValue(long Value) : ParamValue;

public sealed record RealValue(double Value) : ParamValue;

public sealed record BoolValue(bool Value) : ParamValue;

/// <summary>Binary data (NSData). Rare in shortcuts, kept for lossless round-trips.</summary>
public sealed record DataValue(byte[] Value) : ParamValue;

/// <summary>Date (NSDate). Rare in shortcuts, kept for lossless round-trips.</summary>
public sealed record DateValue(DateTime Value) : ParamValue;

/// <summary>A plain plist array.</summary>
public sealed record ArrayValue(IReadOnlyList<ParamValue> Items) : ParamValue
{
    public bool Equals(ArrayValue? other) => other is not null && Items.SequenceEqual(other.Items);
    public override int GetHashCode() => Items.Count;
}

/// <summary>A plain plist dictionary (no WFSerializationType wrapper, or an unknown one kept verbatim).</summary>
public sealed record DictValue(IReadOnlyList<KeyValuePair<string, ParamValue>> Entries) : ParamValue
{
    public ParamValue? this[string key] => Entries.FirstOrDefault(e => e.Key == key).Value;

    public bool Equals(DictValue? other) =>
        other is not null && Entries.Count == other.Entries.Count &&
        Entries.All(e => other[e.Key] is { } v && v.Equals(e.Value));

    public override int GetHashCode() => Entries.Count;
}

/// <summary>
/// Text that may contain variables (WFTextTokenString). Variables are represented by the
/// object replacement character U+FFFC inside <see cref="Text"/>, at the positions listed in
/// <see cref="Attachments"/>.
/// </summary>
public sealed record TokenString(string Text, IReadOnlyList<TokenAttachment> Attachments) : ParamValue
{
    public const char Placeholder = '￼';

    public static TokenString Plain(string text) => new(text, []);

    public static TokenString Of(VariableRef variable) =>
        new(Placeholder.ToString(), [new TokenAttachment(0, variable)]);

    public bool Equals(TokenString? other) =>
        other is not null && Text == other.Text && Attachments.SequenceEqual(other.Attachments);

    public override int GetHashCode() => Text.GetHashCode();
}

public sealed record TokenAttachment(int Position, VariableRef Variable);

/// <summary>A single variable used as a parameter (WFTextTokenAttachment).</summary>
public sealed record VariableValue(VariableRef Variable) : ParamValue;

/// <summary>Dictionary built with the Dictionary action UI (WFDictionaryFieldValue).</summary>
public sealed record DictionaryFieldValue(IReadOnlyList<DictionaryField> Items) : ParamValue
{
    public bool Equals(DictionaryFieldValue? other) => other is not null && Items.SequenceEqual(other.Items);
    public override int GetHashCode() => Items.Count;
}

public enum DictionaryItemType
{
    Text = 0,
    Dictionary = 1,
    Array = 2,
    Number = 3,
    Boolean = 4,
}

public sealed record DictionaryField(DictionaryItemType ItemType, TokenString Key, ParamValue Value);

/// <summary>Any other serialized wrapper ({Value, WFSerializationType}) kept verbatim.</summary>
public sealed record WrappedValue(string SerializationType, ParamValue Value) : ParamValue;
