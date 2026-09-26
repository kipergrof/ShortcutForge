using System.Text;

namespace ShortcutForge.Core.Model;

/// <summary>Find / replace and rename operations over a whole shortcut.</summary>
public static class ShortcutRefactor
{
    private static readonly HashSet<string> StructuralKeys =
        [ActionInstance.UuidKey, ActionInstance.GroupingKey, ActionInstance.ControlFlowKey];

    /// <summary>Indices of the actions whose identifier or text values contain <paramref name="query"/>.</summary>
    public static IReadOnlyList<int> FindActions(Shortcut shortcut, string query, bool matchCase = false,
        Func<ActionInstance, string?>? displayName = null)
    {
        if (string.IsNullOrEmpty(query)) return [];
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var result = new List<int>();
        for (var i = 0; i < shortcut.Actions.Count; i++)
        {
            var action = shortcut.Actions[i];
            if (action.Identifier.Contains(query, comparison) ||
                (displayName?.Invoke(action)?.Contains(query, comparison) ?? false) ||
                action.Parameters.Where(p => !StructuralKeys.Contains(p.Key))
                    .Any(p => Texts(p.Value).Any(t => t.Contains(query, comparison))))
                result.Add(i);
        }
        return result;
    }

    /// <summary>Plain text pieces of a value (variables excluded).</summary>
    private static IEnumerable<string> Texts(ParamValue value) => value switch
    {
        StringValue s => [s.Value],
        TokenString t => [t.Text.Replace(TokenString.Placeholder.ToString(), " ")],
        ArrayValue a => a.Items.SelectMany(Texts),
        DictValue d => d.Entries.SelectMany(e => Texts(e.Value)),
        DictionaryFieldValue f => f.Items.SelectMany(i => Texts(i.Key).Concat(Texts(i.Value))),
        WrappedValue w => Texts(w.Value),
        _ => [],
    };

    /// <summary>
    /// Replaces text in every text value of every action (variables inside texts are kept in
    /// place). Returns the number of replacements.
    /// </summary>
    public static int ReplaceText(Shortcut shortcut, string find, string replacement, bool matchCase = false)
    {
        if (string.IsNullOrEmpty(find)) return 0;
        var count = 0;
        foreach (var action in shortcut.Actions)
            foreach (var key in action.Parameters.Keys.Where(k => !StructuralKeys.Contains(k)).ToList())
                action.Parameters[key] = Replace(action.Parameters[key], find, replacement, matchCase, ref count);
        return count;
    }

    private static ParamValue Replace(ParamValue value, string find, string replacement, bool matchCase, ref int count)
    {
        switch (value)
        {
            case StringValue s:
                return new StringValue(ReplaceIn(s.Value, find, replacement, matchCase, ref count));
            case TokenString t:
                return ReplaceInTokenString(t, find, replacement, matchCase, ref count);
            case ArrayValue a:
            {
                var items = new List<ParamValue>();
                foreach (var item in a.Items) items.Add(Replace(item, find, replacement, matchCase, ref count));
                return new ArrayValue(items);
            }
            case DictValue d:
            {
                var entries = new List<KeyValuePair<string, ParamValue>>();
                foreach (var e in d.Entries)
                    entries.Add(new(e.Key, e.Key is "Type" or "WFSerializationType" ? e.Value : Replace(e.Value, find, replacement, matchCase, ref count)));
                return new DictValue(entries);
            }
            case DictionaryFieldValue f:
            {
                var items = new List<DictionaryField>();
                foreach (var i in f.Items)
                    items.Add(i with
                    {
                        Key = ReplaceInTokenString(i.Key, find, replacement, matchCase, ref count),
                        Value = Replace(i.Value, find, replacement, matchCase, ref count),
                    });
                return new DictionaryFieldValue(items);
            }
            case WrappedValue w:
                return w with { Value = Replace(w.Value, find, replacement, matchCase, ref count) };
            default:
                return value;
        }
    }

    /// <summary>Replaces only inside the text segments between variables.</summary>
    private static TokenString ReplaceInTokenString(TokenString t, string find, string replacement, bool matchCase, ref int count)
    {
        var byPosition = t.Attachments.ToDictionary(a => a.Position, a => a.Variable);
        var text = new StringBuilder();
        var attachments = new List<TokenAttachment>();
        var segment = new StringBuilder();

        void Flush(ref int c)
        {
            text.Append(ReplaceIn(segment.ToString(), find, replacement, matchCase, ref c));
            segment.Clear();
        }

        for (var i = 0; i < t.Text.Length; i++)
        {
            if (t.Text[i] == TokenString.Placeholder && byPosition.TryGetValue(i, out var variable))
            {
                Flush(ref count);
                attachments.Add(new TokenAttachment(text.Length, variable));
                text.Append(TokenString.Placeholder);
            }
            else segment.Append(t.Text[i]);
        }
        Flush(ref count);
        return new TokenString(text.ToString(), attachments);
    }

    private static string ReplaceIn(string text, string find, string replacement, bool matchCase, ref int count)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = text.IndexOf(find, comparison);
        if (index < 0) return text;
        var sb = new StringBuilder();
        var start = 0;
        while (index >= 0)
        {
            sb.Append(text, start, index - start).Append(replacement);
            count++;
            start = index + find.Length;
            index = text.IndexOf(find, start, comparison);
        }
        return sb.Append(text, start, text.Length - start).ToString();
    }

    /// <summary>Names of the named variables (Set Variable / Add to Variable) in the shortcut.</summary>
    public static IReadOnlyList<string> NamedVariables(Shortcut shortcut) =>
        shortcut.Actions
            .Where(a => a.Identifier is "is.workflow.actions.setvariable" or "is.workflow.actions.appendvariable")
            .Select(a => a.Get("WFVariableName") as StringValue)
            .Where(v => v is not null && v.Value.Length > 0)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

    /// <summary>
    /// Renames a named variable everywhere: in Set / Add to Variable actions and in every
    /// reference to it. Returns the number of changed places.
    /// </summary>
    public static int RenameVariable(Shortcut shortcut, string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return 0;
        var count = 0;
        foreach (var action in shortcut.Actions)
        {
            if (action.Identifier is "is.workflow.actions.setvariable" or "is.workflow.actions.appendvariable" &&
                action.Get("WFVariableName") is StringValue { Value: var name } && name == oldName)
            {
                action.Parameters["WFVariableName"] = new StringValue(newName);
                count++;
            }
            foreach (var key in action.Parameters.Keys.ToList())
                action.Parameters[key] = MapVariables(action.Parameters[key], v =>
                {
                    if (v.Kind != VariableKinds.Variable || v.VariableName != oldName) return v;
                    count++;
                    return v with { VariableName = newName };
                });
        }
        return count;
    }

    private static ParamValue MapVariables(ParamValue value, Func<VariableRef, VariableRef> map) => value switch
    {
        VariableValue v => new VariableValue(map(v.Variable)),
        TokenString t => t with { Attachments = t.Attachments.Select(a => a with { Variable = map(a.Variable) }).ToList() },
        ArrayValue a => new ArrayValue(a.Items.Select(i => MapVariables(i, map)).ToList()),
        DictValue d => new DictValue(d.Entries.Select(e => new KeyValuePair<string, ParamValue>(e.Key, MapVariables(e.Value, map))).ToList()),
        DictionaryFieldValue f => new DictionaryFieldValue(f.Items.Select(i =>
            i with { Key = (TokenString)MapVariables(i.Key, map), Value = MapVariables(i.Value, map) }).ToList()),
        WrappedValue w => w with { Value = MapVariables(w.Value, map) },
        _ => value,
    };
}
