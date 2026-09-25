namespace ShortcutForge.Core.Model;

public enum ControlFlowMode
{
    Start = 0,
    Middle = 1,
    End = 2,
}

/// <summary>One action of a shortcut: an identifier plus its parameter dictionary.</summary>
public sealed class ActionInstance
{
    public const string UuidKey = "UUID";
    public const string GroupingKey = "GroupingIdentifier";
    public const string ControlFlowKey = "WFControlFlowMode";
    public const string CustomOutputNameKey = "CustomOutputName";

    public ActionInstance(string identifier, Dictionary<string, ParamValue>? parameters = null)
    {
        Identifier = identifier;
        Parameters = parameters ?? new Dictionary<string, ParamValue>();
    }

    public string Identifier { get; set; }

    public Dictionary<string, ParamValue> Parameters { get; }

    public string? Uuid
    {
        get => GetString(UuidKey);
        set => SetOrRemove(UuidKey, value is null ? null : new StringValue(value));
    }

    public string? GroupingIdentifier
    {
        get => GetString(GroupingKey);
        set => SetOrRemove(GroupingKey, value is null ? null : new StringValue(value));
    }

    public ControlFlowMode? ControlFlow
    {
        get => Parameters.TryGetValue(ControlFlowKey, out var v) && v is IntegerValue i ? (ControlFlowMode)i.Value : null;
        set => SetOrRemove(ControlFlowKey, value is null ? null : new IntegerValue((long)value.Value));
    }

    public string? CustomOutputName => GetString(CustomOutputNameKey);

    /// <summary>Returns this action's UUID, creating one if it has none.</summary>
    public string EnsureUuid() => Uuid ??= NewUuid();

    public static string NewUuid() => Guid.NewGuid().ToString().ToUpperInvariant();

    public ParamValue? Get(string key) => Parameters.TryGetValue(key, out var v) ? v : null;

    public void SetOrRemove(string key, ParamValue? value)
    {
        if (value is null) Parameters.Remove(key);
        else Parameters[key] = value;
    }

    private string? GetString(string key) => Get(key) is StringValue s ? s.Value : null;

    public ActionInstance Clone() => new(Identifier, new Dictionary<string, ParamValue>(Parameters));

    public override string ToString() => Identifier;
}
