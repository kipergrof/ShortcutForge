using System.Globalization;
using System.Text.RegularExpressions;
using Claunia.PropertyList;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Plist;

/// <summary>Converts between <see cref="Shortcut"/> and the Shortcuts app's plist format.</summary>
public static partial class PlistSerializer
{
    private const string SerializationTypeKey = "WFSerializationType";

    private static readonly string[] KnownTopLevelKeys =
    [
        "WFWorkflowActions", "WFWorkflowIcon", "WFWorkflowInputContentItemClasses",
        "WFWorkflowTypes", "WFWorkflowClientVersion", "WFWorkflowName",
    ];

    // ---------------------------------------------------------------- reading

    public static Shortcut Read(byte[] plistBytes, string? name = null)
    {
        var root = PropertyListParser.Parse(plistBytes) as NSDictionary
                   ?? throw new FormatException(L.T("A fájl nem shortcut plist (a gyökér nem szótár).", "The file is not a shortcut plist (the root is not a dictionary)."));
        return FromDictionary(root, name);
    }

    public static Shortcut FromDictionary(NSDictionary root, string? name = null)
    {
        if (!root.ContainsKey("WFWorkflowActions"))
            throw new FormatException(L.T("A plist nem tartalmaz WFWorkflowActions kulcsot.", "The plist has no WFWorkflowActions key."));

        var shortcut = new Shortcut();
        if (name is not null) shortcut.Name = name;
        else if (root.TryGetValue("WFWorkflowName", out var n) && n is NSString ns) shortcut.Name = ns.Content;

        if (root.TryGetValue("WFWorkflowClientVersion", out var cv))
            shortcut.ClientVersion = cv.ToString() ?? Shortcut.DefaultClientVersion;

        if (root.TryGetValue("WFWorkflowIcon", out var iconObj) && iconObj is NSDictionary icon)
        {
            long color = icon.TryGetValue("WFWorkflowIconStartColor", out var c) && c is NSNumber cn ? cn.ToLong() : ShortcutIcon.Default.StartColor;
            long glyph = icon.TryGetValue("WFWorkflowIconGlyphNumber", out var g) && g is NSNumber gn ? gn.ToLong() : ShortcutIcon.Default.GlyphNumber;
            shortcut.Icon = new ShortcutIcon(color, glyph);
        }

        shortcut.InputClasses = ReadStringArray(root, "WFWorkflowInputContentItemClasses");
        shortcut.Types = ReadStringArray(root, "WFWorkflowTypes");

        foreach (var obj in (NSArray)root["WFWorkflowActions"])
        {
            if (obj is not NSDictionary a) continue;
            var id = a.TryGetValue("WFWorkflowActionIdentifier", out var idObj) ? idObj.ToString()! : "";
            var action = new ActionInstance(id);
            if (a.TryGetValue("WFWorkflowActionParameters", out var p) && p is NSDictionary pd)
                foreach (var (key, value) in pd)
                    action.Parameters[key] = ToParam(value);
            shortcut.Actions.Add(action);
        }

        foreach (var (key, value) in root)
            if (!KnownTopLevelKeys.Contains(key))
                shortcut.Extra[key] = ToParam(value);

        return shortcut;
    }

    private static List<string> ReadStringArray(NSDictionary root, string key) =>
        root.TryGetValue(key, out var v) && v is NSArray arr
            ? arr.Select(o => o.ToString() ?? "").ToList()
            : [];

    public static ParamValue ToParam(NSObject obj)
    {
        switch (obj)
        {
            case NSString s:
                return new StringValue(s.Content);
            case NSNumber n:
                return n.GetNSNumberType() switch
                {
                    NSNumber.BOOLEAN => new BoolValue(n.ToBool()),
                    NSNumber.INTEGER => new IntegerValue(n.ToLong()),
                    _ => new RealValue(n.ToDouble()),
                };
            case NSData d:
                return new DataValue(d.Bytes);
            case NSDate date:
                return new DateValue(date.Date);
            case NSArray arr:
                return new ArrayValue(arr.Select(ToParam).ToList());
            case NSDictionary dict:
                return DictToParam(dict);
            case UID uid:
                return new IntegerValue((long)uid.ToUInt64());
            default:
                return new StringValue(obj.ToString() ?? "");
        }
    }

    private static ParamValue DictToParam(NSDictionary dict)
    {
        if (dict.TryGetValue(SerializationTypeKey, out var typeObj) && typeObj is NSString type &&
            dict.TryGetValue("Value", out var value) && dict.Count == 2)
        {
            switch (type.Content)
            {
                case "WFTextTokenString" when value is NSDictionary tv:
                    return ReadTokenString(tv);
                case "WFTextTokenAttachment" when value is NSDictionary av:
                    return new VariableValue(ReadVariable(av));
                case "WFDictionaryFieldValue" when value is NSDictionary dv:
                    return ReadDictionaryField(dv);
                default:
                    return new WrappedValue(type.Content, ToParam(value));
            }
        }

        return new DictValue(dict.Select(kv => new KeyValuePair<string, ParamValue>(kv.Key, ToParam(kv.Value))).ToList());
    }

    private static TokenString ReadTokenString(NSDictionary value)
    {
        var text = value.TryGetValue("string", out var s) ? s.ToString() ?? "" : "";
        var attachments = new List<TokenAttachment>();
        if (value.TryGetValue("attachmentsByRange", out var r) && r is NSDictionary ranges)
        {
            foreach (var (rangeKey, att) in ranges)
            {
                if (att is not NSDictionary ad) continue;
                var m = RangeRegex().Match(rangeKey);
                if (!m.Success) continue;
                attachments.Add(new TokenAttachment(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), ReadVariable(ad)));
            }
        }

        attachments.Sort((a, b) => a.Position.CompareTo(b.Position));

        // Some producers omit the placeholder; make sure every attachment has one.
        var chars = text.ToCharArray();
        if (attachments.Any(a => a.Position >= chars.Length || chars[a.Position] != TokenString.Placeholder))
        {
            var builder = new System.Text.StringBuilder(text);
            foreach (var a in attachments)
            {
                if (a.Position > builder.Length) continue;
                if (a.Position == builder.Length || builder[a.Position] != TokenString.Placeholder)
                    builder.Insert(a.Position, TokenString.Placeholder);
            }
            text = builder.ToString();
        }

        return new TokenString(text, attachments);
    }

    public static VariableRef ReadVariable(NSDictionary att)
    {
        string? Str(string k) => att.TryGetValue(k, out var v) ? v.ToString() : null;
        var extra = att.Where(kv => kv.Key is not ("Type" or "OutputUUID" or "OutputName" or "VariableName"))
            .Select(kv => new KeyValuePair<string, ParamValue>(kv.Key, ToParam(kv.Value)))
            .ToList();
        return new VariableRef(
            Str("Type") ?? VariableKinds.ActionOutput,
            Str("OutputUUID"),
            Str("OutputName"),
            Str("VariableName"),
            extra.Count > 0 ? new DictValue(extra) : null);
    }

    private static DictionaryFieldValue ReadDictionaryField(NSDictionary value)
    {
        var items = new List<DictionaryField>();
        if (value.TryGetValue("WFDictionaryFieldValueItems", out var arrObj) && arrObj is NSArray arr)
        {
            foreach (var itemObj in arr)
            {
                if (itemObj is not NSDictionary item) continue;
                var type = item.TryGetValue("WFItemType", out var t) && t is NSNumber tn ? (DictionaryItemType)tn.ToInt() : DictionaryItemType.Text;
                var key = item.TryGetValue("WFKey", out var k) ? AsTokenString(ToParam(k)) : TokenString.Plain("");
                var val = item.TryGetValue("WFValue", out var v) ? ToParam(v) : TokenString.Plain("");
                items.Add(new DictionaryField(type, key, val));
            }
        }
        return new DictionaryFieldValue(items);
    }

    private static TokenString AsTokenString(ParamValue v) => v switch
    {
        TokenString ts => ts,
        StringValue s => TokenString.Plain(s.Value),
        VariableValue vv => TokenString.Of(vv.Variable),
        _ => TokenString.Plain(""),
    };

    [GeneratedRegex(@"^\{\s*(\d+)\s*,\s*(\d+)\s*\}$")]
    private static partial Regex RangeRegex();

    // ---------------------------------------------------------------- writing

    public static NSDictionary ToDictionary(Shortcut shortcut)
    {
        var root = new NSDictionary();
        foreach (var (key, value) in shortcut.Extra)
            root[key] = ToNS(value);

        root["WFWorkflowClientVersion"] = new NSString(shortcut.ClientVersion);
        root["WFWorkflowIcon"] = new NSDictionary
        {
            ["WFWorkflowIconStartColor"] = new NSNumber(shortcut.Icon.StartColor),
            ["WFWorkflowIconGlyphNumber"] = new NSNumber(shortcut.Icon.GlyphNumber),
        };
        root["WFWorkflowInputContentItemClasses"] = new NSArray(shortcut.InputClasses.Select(s => (NSObject)new NSString(s)).ToArray());
        root["WFWorkflowTypes"] = new NSArray(shortcut.Types.Select(s => (NSObject)new NSString(s)).ToArray());

        if (!root.ContainsKey("WFWorkflowImportQuestions")) root["WFWorkflowImportQuestions"] = new NSArray();
        if (!root.ContainsKey("WFWorkflowMinimumClientVersion")) root["WFWorkflowMinimumClientVersion"] = new NSNumber(900);
        if (!root.ContainsKey("WFWorkflowMinimumClientVersionString")) root["WFWorkflowMinimumClientVersionString"] = new NSString("900");

        var actions = new NSArray();
        foreach (var action in shortcut.Actions)
        {
            var parameters = new NSDictionary();
            foreach (var (key, value) in action.Parameters)
                parameters[key] = ToNS(value);
            actions.Add(new NSDictionary
            {
                ["WFWorkflowActionIdentifier"] = new NSString(action.Identifier),
                ["WFWorkflowActionParameters"] = parameters,
            });
        }
        root["WFWorkflowActions"] = actions;
        return root;
    }

    /// <summary>Binary plist, the format the Shortcuts app uses for .shortcut files.</summary>
    public static byte[] WriteBinary(Shortcut shortcut) => BinaryPropertyListWriter.WriteToArray(ToDictionary(shortcut));

    public static string WriteXml(Shortcut shortcut) => ToDictionary(shortcut).ToXmlPropertyList();

    public static NSObject ToNS(ParamValue value) => value switch
    {
        StringValue s => new NSString(s.Value),
        IntegerValue i => new NSNumber(i.Value),
        RealValue r => new NSNumber(r.Value),
        BoolValue b => new NSNumber(b.Value),
        DataValue d => new NSData(d.Value),
        DateValue d => new NSDate(d.Value),
        ArrayValue a => new NSArray(a.Items.Select(ToNS).ToArray()),
        DictValue d => DictToNS(d),
        TokenString t => Wrap("WFTextTokenString", TokenStringToNS(t)),
        VariableValue v => Wrap("WFTextTokenAttachment", VariableToNS(v.Variable)),
        DictionaryFieldValue f => Wrap("WFDictionaryFieldValue", new NSDictionary
        {
            ["WFDictionaryFieldValueItems"] = new NSArray(f.Items.Select(item => (NSObject)new NSDictionary
            {
                ["WFItemType"] = new NSNumber((int)item.ItemType),
                ["WFKey"] = ToNS(item.Key),
                ["WFValue"] = ToNS(item.Value),
            }).ToArray()),
        }),
        WrappedValue w => Wrap(w.SerializationType, ToNS(w.Value)),
        _ => throw new NotSupportedException(value.GetType().Name),
    };

    private static NSDictionary DictToNS(DictValue d)
    {
        var dict = new NSDictionary();
        foreach (var (k, v) in d.Entries) dict[k] = ToNS(v);
        return dict;
    }

    private static NSDictionary Wrap(string type, NSObject value) => new()
    {
        ["Value"] = value,
        [SerializationTypeKey] = new NSString(type),
    };

    private static NSDictionary TokenStringToNS(TokenString t)
    {
        var ranges = new NSDictionary();
        foreach (var a in t.Attachments)
            ranges[$"{{{a.Position}, 1}}"] = VariableToNS(a.Variable);
        return new NSDictionary
        {
            ["string"] = new NSString(t.Text),
            ["attachmentsByRange"] = ranges,
        };
    }

    public static NSDictionary VariableToNS(VariableRef v)
    {
        var dict = new NSDictionary { ["Type"] = new NSString(v.Kind) };
        if (v.OutputUuid is not null) dict["OutputUUID"] = new NSString(v.OutputUuid);
        if (v.OutputName is not null) dict["OutputName"] = new NSString(v.OutputName);
        if (v.VariableName is not null) dict["VariableName"] = new NSString(v.VariableName);
        if (v.Extra is not null)
            foreach (var (k, val) in v.Extra.Entries) dict[k] = ToNS(val);
        return dict;
    }
}
