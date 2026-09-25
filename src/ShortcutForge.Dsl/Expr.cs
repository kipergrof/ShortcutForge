using ShortcutForge.Core.Model;

namespace ShortcutForge.Dsl;

/// <summary>Expression nodes of the DSL (parameter values).</summary>
public abstract record Expr(SourcePos Pos);

/// <summary>String literal; <see cref="Parts"/> are either text or interpolated variables.</summary>
public sealed record StringExpr(SourcePos Pos, IReadOnlyList<object> Parts) : Expr(Pos)
{
    public bool HasInterpolation => Parts.Any(p => p is VariableRef);
    public string PlainText => string.Concat(Parts.OfType<string>());
}

public sealed record NumberExpr(SourcePos Pos, string Text, bool IsInteger) : Expr(Pos);

public sealed record BoolExpr(SourcePos Pos, bool Value) : Expr(Pos);

public sealed record VariableExpr(SourcePos Pos, VariableRef Variable) : Expr(Pos);

public sealed record ListExpr(SourcePos Pos, IReadOnlyList<Expr> Items) : Expr(Pos);

public sealed record DictExpr(SourcePos Pos, IReadOnlyList<(Expr Key, Expr Value)> Entries) : Expr(Pos);

/// <summary>Explicitly typed raw forms: text(...), fields(...), wrap(...), data(...), date(...).</summary>
public sealed record FormExpr(SourcePos Pos, string Form, IReadOnlyList<Expr> Args) : Expr(Pos);
