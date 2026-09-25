using System.Globalization;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Dsl;

/// <summary>Turns parsed expressions into plist parameter values, guided by the parameter kind.</summary>
public static class ValueConverter
{
    public static ParamValue Convert(Expr expr, ParamKind kind)
    {
        if (expr is FormExpr { Form: "raw", Args: [var rawInner] }) return Raw(rawInner);
        switch (kind)
        {
            case ParamKind.Text:
                switch (expr)
                {
                    case StringExpr s: return ToTokenString(s);
                    case VariableExpr v: return TokenString.Of(v.Variable);
                    case NumberExpr n: return TokenString.Plain(n.Text);
                }
                break;

            case ParamKind.String:
            case ParamKind.Enum:
                if (expr is StringExpr { HasInterpolation: false } plain) return new StringValue(plain.PlainText);
                if (expr is StringExpr withVars) return ToTokenString(withVars);
                break;

            case ParamKind.Variable:
                switch (expr)
                {
                    case VariableExpr v: return new VariableValue(v.Variable);
                    case StringExpr s: return ToTokenString(s);
                }
                break;

            case ParamKind.Number:
            case ParamKind.Integer:
                switch (expr)
                {
                    case NumberExpr n: return Number(n);
                    case VariableExpr v: return new VariableValue(v.Variable);
                    case StringExpr { HasInterpolation: false } s: return new StringValue(s.PlainText);
                    case StringExpr s: return ToTokenString(s);
                }
                break;

            case ParamKind.Bool:
                switch (expr)
                {
                    case BoolExpr b: return new BoolValue(b.Value);
                    case VariableExpr v: return new VariableValue(v.Variable);
                }
                break;

            case ParamKind.Dictionary:
                if (expr is DictExpr d) return ToDictionaryField(d);
                break;

            case ParamKind.List:
                if (expr is ListExpr l)
                    return new ArrayValue(l.Items.Select(ToListItem).ToList());
                break;
        }

        return Raw(expr);
    }

    /// <summary>Kind-independent conversion. Every raw form maps back to exactly one value.</summary>
    public static ParamValue Raw(Expr expr) => expr switch
    {
        StringExpr { HasInterpolation: false } s => new StringValue(s.PlainText),
        StringExpr s => ToTokenString(s),
        NumberExpr n => Number(n),
        BoolExpr b => new BoolValue(b.Value),
        VariableExpr v => new VariableValue(v.Variable),
        ListExpr l => new ArrayValue(l.Items.Select(Raw).ToList()),
        DictExpr d => new DictValue(d.Entries.Select(e =>
            new KeyValuePair<string, ParamValue>(KeyText(e.Key), Raw(e.Value))).ToList()),
        FormExpr f => Form(f),
        _ => throw new DslException(L.T("Ismeretlen kifejezés.", "Unknown expression."), expr.Pos),
    };

    private static ParamValue Form(FormExpr f)
    {
        switch (f.Form)
        {
            case "text":
                return f.Args is [StringExpr s] ? ToTokenString(s) : throw Error(f, L.T("text(\"…\") egy szöveget vár.", "text(\"…\") expects a text."));
            case "fields":
                return f.Args is [DictExpr d] ? ToDictionaryField(d) : throw Error(f, L.T("fields({…}) egy szótárat vár.", "fields({…}) expects a dictionary."));
            case "wrap":
                if (f.Args is [StringExpr { HasInterpolation: false } type, var inner])
                {
                    // Normalize through the plist reader so e.g. wrap("WFTextTokenString", …) becomes a TokenString.
                    var wrapped = new WrappedValue(type.PlainText, Raw(inner));
                    return PlistSerializer.ToParam(PlistSerializer.ToNS(wrapped));
                }
                throw Error(f, L.T("wrap(\"Típus\", érték) formát vár.", "Expected the form wrap(\"Type\", value)."));
            case "data":
                if (f.Args is [StringExpr { HasInterpolation: false } b64])
                {
                    try { return new DataValue(System.Convert.FromBase64String(b64.PlainText)); }
                    catch (FormatException) { throw Error(f, L.T("Hibás base64 adat.", "Invalid base64 data.")); }
                }
                throw Error(f, L.T("data(\"base64\") formát vár.", "Expected the form data(\"base64\")."));
            case "raw":
                return f.Args is [var r] ? Raw(r) : throw Error(f, L.T("raw(érték) egy értéket vár.", "raw(value) expects one value."));
            case "date":
                if (f.Args is [StringExpr { HasInterpolation: false } iso] &&
                    DateTime.TryParse(iso.PlainText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    return new DateValue(dt);
                throw Error(f, L.T("date(\"ISO dátum\") formát vár.", "Expected the form date(\"ISO date\")."));
            default:
                throw Error(f, L.T($"Ismeretlen forma: {f.Form}", $"Unknown form: {f.Form}"));
        }
    }

    private static DslException Error(Expr e, string message) => new(message, e.Pos);

    public static ParamValue Number(NumberExpr n)
    {
        if (n.IsInteger && long.TryParse(n.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
            return new IntegerValue(l);
        if (double.TryParse(n.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return new RealValue(d);
        throw new DslException(L.T($"Hibás szám: {n.Text}", $"Invalid number: {n.Text}"), n.Pos);
    }

    public static TokenString ToTokenString(StringExpr s)
    {
        var text = new System.Text.StringBuilder();
        var attachments = new List<TokenAttachment>();
        foreach (var part in s.Parts)
        {
            if (part is string str) text.Append(str);
            else if (part is VariableRef v)
            {
                attachments.Add(new TokenAttachment(text.Length, v));
                text.Append(TokenString.Placeholder);
            }
        }
        return new TokenString(text.ToString(), attachments);
    }

    private static string KeyText(Expr key) => key switch
    {
        StringExpr { HasInterpolation: false } s => s.PlainText,
        _ => throw new DslException(L.T("A szótár kulcsa egyszerű szöveg kell legyen.", "Dictionary keys must be plain text."), key.Pos),
    };

    private static TokenString KeyToken(Expr key) => key switch
    {
        StringExpr s => ToTokenString(s),
        VariableExpr v => TokenString.Of(v.Variable),
        _ => throw new DslException(L.T("A szótár kulcsa szöveg vagy változó kell legyen.", "Dictionary keys must be text or a variable."), key.Pos),
    };

    /// <summary>Converts <c>{ "key": value }</c> to the Dictionary action's field format.</summary>
    public static DictionaryFieldValue ToDictionaryField(DictExpr d) =>
        new(d.Entries.Select(e => DictionaryItem(KeyToken(e.Key), e.Value)).ToList());

    private static DictionaryField DictionaryItem(TokenString key, Expr value) => value switch
    {
        StringExpr s => new DictionaryField(DictionaryItemType.Text, key, ToTokenString(s)),
        VariableExpr v => new DictionaryField(DictionaryItemType.Text, key, TokenString.Of(v.Variable)),
        NumberExpr n => new DictionaryField(DictionaryItemType.Number, key, TokenString.Plain(n.Text)),
        BoolExpr b => new DictionaryField(DictionaryItemType.Boolean, key,
            new WrappedValue("WFNumberSubstitutableState", new BoolValue(b.Value))),
        DictExpr nested => new DictionaryField(DictionaryItemType.Dictionary, key, ToDictionaryField(nested)),
        ListExpr list => new DictionaryField(DictionaryItemType.Array, key,
            new WrappedValue("WFArrayParameterState", new ArrayValue(list.Items.Select(ToListItem).ToList()))),
        _ => throw new DslException(L.T("Ez az érték nem használható szótárban.", "This value cannot be used in a dictionary."), value.Pos),
    };

    /// <summary>List items as the List action stores them: {WFItemType, WFValue}.</summary>
    private static ParamValue ToListItem(Expr item)
    {
        var (type, value) = item switch
        {
            StringExpr s => (DictionaryItemType.Text, (ParamValue)ToTokenString(s)),
            VariableExpr v => (DictionaryItemType.Text, TokenString.Of(v.Variable)),
            NumberExpr n => (DictionaryItemType.Number, TokenString.Plain(n.Text)),
            BoolExpr b => (DictionaryItemType.Boolean, new WrappedValue("WFNumberSubstitutableState", new BoolValue(b.Value))),
            DictExpr d => (DictionaryItemType.Dictionary, ToDictionaryField(d)),
            ListExpr l => (DictionaryItemType.Array, new WrappedValue("WFArrayParameterState", new ArrayValue(l.Items.Select(ToListItem).ToList()))),
            _ => throw new DslException(L.T("Ez az érték nem használható listában.", "This value cannot be used in a list."), item.Pos),
        };
        return new DictValue([
            new KeyValuePair<string, ParamValue>("WFItemType", new IntegerValue((int)type)),
            new KeyValuePair<string, ParamValue>("WFValue", value),
        ]);
    }
}
