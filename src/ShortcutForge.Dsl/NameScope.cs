using ShortcutForge.Core.Model;

namespace ShortcutForge.Dsl;

/// <summary>
/// Maps DSL identifiers to variables: built-ins (ShortcutInput, RepeatItem, ...),
/// magic variables (action outputs bound with <c>x = Action(...)</c>) and named variables (<c>var x = ...</c>).
/// </summary>
public sealed class NameScope
{
    public static readonly IReadOnlyDictionary<string, VariableRef> Builtins = new Dictionary<string, VariableRef>
    {
        ["ShortcutInput"] = VariableRef.Global(VariableKinds.ShortcutInput),
        ["Clipboard"] = VariableRef.Global(VariableKinds.Clipboard),
        ["CurrentDate"] = VariableRef.Global(VariableKinds.CurrentDate),
        ["Ask"] = VariableRef.Global(VariableKinds.Ask),
        ["DeviceDetails"] = VariableRef.Global(VariableKinds.DeviceDetails),
        ["RepeatItem"] = VariableRef.Named(VariableRef.RepeatItem),
        ["RepeatIndex"] = VariableRef.Named(VariableRef.RepeatIndex),
    };

    /// <summary>Words that cannot be used as variable names.</summary>
    public static readonly HashSet<string> Keywords =
    [
        "if", "else", "repeat", "foreach", "menu", "case", "var", "action", "true", "false", "_",
    ];

    /// <summary>Explicit value forms, recognized only when followed by '('.</summary>
    public static readonly HashSet<string> Forms = ["text", "fields", "wrap", "data", "date", "output", "var", "raw", "global"];

    private readonly Dictionary<string, VariableRef> _outputs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _namedVariables = new(StringComparer.Ordinal);

    public IEnumerable<string> OutputNames => _outputs.Keys;
    public IEnumerable<string> NamedVariables => _namedVariables;

    public void BindOutput(string name, VariableRef output) => _outputs[name] = output;

    public void DeclareVariable(string name) => _namedVariables.Add(name);

    public bool IsDeclaredVariable(string name) => _namedVariables.Contains(name);

    public VariableRef? Resolve(string name)
    {
        if (Builtins.TryGetValue(name, out var builtin)) return builtin;
        if (_outputs.TryGetValue(name, out var output)) return output;
        if (_namedVariables.Contains(name)) return VariableRef.Named(name);
        return null;
    }

    /// <summary>Finds the identifier that refers to <paramref name="variable"/> (ignoring Extra), if any.</summary>
    public string? NameOf(VariableRef variable)
    {
        var bare = variable with { Extra = null };
        foreach (var (name, v) in Builtins)
            if (v.Equals(bare)) return name;
        if (bare.Kind == VariableKinds.ActionOutput)
            foreach (var (name, v) in _outputs)
                if (v.OutputUuid == bare.OutputUuid && v.OutputName == bare.OutputName) return name;
        if (bare.Kind == VariableKinds.Variable && bare.VariableName is { } vn && _namedVariables.Contains(vn) &&
            bare.OutputUuid is null && bare.OutputName is null && IsIdentifier(vn))
            return vn;
        return null;
    }

    public NameScope Clone()
    {
        var copy = new NameScope();
        foreach (var (k, v) in _outputs) copy._outputs[k] = v;
        foreach (var v in _namedVariables) copy._namedVariables.Add(v);
        return copy;
    }

    public static bool IsIdentifier(string name) =>
        name.Length > 0 && Lexer.IsIdentStart(name[0]) && name.All(Lexer.IsIdentPart) &&
        !Keywords.Contains(name) && !Builtins.ContainsKey(name);
}

