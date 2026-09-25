using System.Globalization;
using System.Text;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Dsl;

public enum TokenKind
{
    Identifier,
    String,
    Number,
    Directive,   // #name
    LineComment, // // text
    Symbol,      // ( ) { } [ ] , : = . += -> == != < <= > >= !
    NewLine,
    End,
}

/// <summary>Part of a string literal: either literal text or an interpolated expression.</summary>
public abstract record StringPart;
public sealed record TextPart(string Text) : StringPart;
public sealed record InterpolationPart(IReadOnlyList<Token> Tokens, SourcePos Pos) : StringPart;

public readonly record struct SourcePos(int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}

public sealed record Token(TokenKind Kind, string Text, SourcePos Pos, int Offset, int Length)
{
    public IReadOnlyList<StringPart>? Parts { get; init; }

    public bool Is(string symbolOrWord) =>
        (Kind is TokenKind.Symbol or TokenKind.Identifier) && Text == symbolOrWord;

    public override string ToString() => Kind switch
    {
        TokenKind.End => L.T("fájl vége", "end of file"),
        TokenKind.NewLine => L.T("sor vége", "end of line"),
        TokenKind.String => L.T("szöveg", "text"),
        _ => $"'{Text}'",
    };
}

public sealed class DslException(string message, SourcePos pos, int offset = -1, int length = 0) : Exception(message)
{
    public SourcePos Pos { get; } = pos;
    public int Offset { get; } = offset;
    public int Length { get; } = length;

    public override string ToString() => $"{Pos.Line}. sor, {Pos.Column}. oszlop: {Message}";
}

public sealed class Lexer
{
    private static readonly string[] Symbols = ["+=", "->", "==", "!=", "<=", ">=", "(", ")", "{", "}", "[", "]", ",", ":", "=", ".", "<", ">", "!", ";", "?"];

    private readonly string _src;
    private int _i;
    private int _line = 1;
    private int _col = 1;

    private Lexer(string src, int start, int line, int col)
    {
        _src = src;
        _i = start;
        _line = line;
        _col = col;
    }

    public static List<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source.Replace("\r\n", "\n"), 0, 1, 1);
        var tokens = lexer.LexUntil(null);
        return tokens;
    }

    private SourcePos Pos => new(_line, _col);

    private char Peek(int ahead = 0) => _i + ahead < _src.Length ? _src[_i + ahead] : '\0';

    private char Next()
    {
        var c = _src[_i++];
        if (c == '\n') { _line++; _col = 1; }
        else _col++;
        return c;
    }

    /// <summary>Lexes until end of input, or until the unmatched closing brace when <paramref name="closing"/> is '}'.</summary>
    private List<Token> LexUntil(char? closing)
    {
        var tokens = new List<Token>();
        var depth = 0;
        while (true)
        {
            SkipSpaces();
            var pos = Pos;
            var start = _i;
            if (_i >= _src.Length)
            {
                if (closing is not null) throw new DslException(L.T("Lezáratlan {…} kifejezés a szövegben.", "Unterminated {…} expression in text."), pos, start);
                tokens.Add(new Token(TokenKind.End, "", pos, start, 0));
                return tokens;
            }

            var c = Peek();
            if (c == '\n')
            {
                Next();
                if (closing is null) tokens.Add(new Token(TokenKind.NewLine, "\n", pos, start, 1));
                continue;
            }

            if (c == '/' && Peek(1) == '/')
            {
                // A "//" line on its own is a Comment action; after code on the same line it is ignored.
                var trailing = tokens.Count > 0 && tokens[^1].Kind != TokenKind.NewLine && tokens[^1].Pos.Line == pos.Line;
                Next(); Next();
                if (Peek() == ' ') Next();
                var sb = new StringBuilder();
                while (_i < _src.Length && Peek() != '\n') sb.Append(Next());
                if (!trailing && closing is null)
                    tokens.Add(new Token(TokenKind.LineComment, sb.ToString(), pos, start, _i - start));
                continue;
            }

            if (c == '/' && Peek(1) == '*')
            {
                Next(); Next();
                while (_i < _src.Length && !(Peek() == '*' && Peek(1) == '/')) Next();
                if (_i >= _src.Length) throw new DslException(L.T("Lezáratlan /* megjegyzés.", "Unterminated /* comment."), pos, start);
                Next(); Next();
                continue;
            }

            if (closing is not null)
            {
                if (c == '{') depth++;
                if (c == '}')
                {
                    if (depth == 0)
                    {
                        Next();
                        tokens.Add(new Token(TokenKind.End, "", pos, start, 0));
                        return tokens;
                    }
                    depth--;
                }
            }

            if (c == '"')
            {
                tokens.Add(LexString(pos, start));
                continue;
            }

            if (c == '#')
            {
                Next();
                var word = ReadWord();
                if (word.Length == 0) throw new DslException(L.T("Hiányzó direktíva név a # után.", "Missing directive name after #."), pos, start);
                tokens.Add(new Token(TokenKind.Directive, word, pos, start, _i - start));
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && char.IsDigit(Peek(1))))
            {
                var sb = new StringBuilder();
                sb.Append(Next());
                while (char.IsDigit(Peek()) || Peek() == '.' ||
                       ((Peek() is 'e' or 'E') && (char.IsDigit(Peek(1)) || Peek(1) is '-' or '+')) ||
                       ((Peek() is '-' or '+') && (sb[^1] is 'e' or 'E')))
                    sb.Append(Next());
                tokens.Add(new Token(TokenKind.Number, sb.ToString(), pos, start, _i - start));
                continue;
            }

            if (IsIdentStart(c))
            {
                var word = ReadWord();
                tokens.Add(new Token(TokenKind.Identifier, word, pos, start, _i - start));
                continue;
            }

            var symbol = Symbols.FirstOrDefault(s => string.CompareOrdinal(_src, _i, s, 0, s.Length) == 0);
            if (symbol is null) throw new DslException(L.T($"Váratlan karakter: '{c}'", $"Unexpected character: '{c}'"), pos, start, 1);
            for (var k = 0; k < symbol.Length; k++) Next();
            tokens.Add(new Token(TokenKind.Symbol, symbol, pos, start, symbol.Length));
        }
    }

    private void SkipSpaces()
    {
        while (_i < _src.Length && Peek() is ' ' or '\t' or '\r') Next();
    }

    public static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    public static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private string ReadWord()
    {
        var sb = new StringBuilder();
        while (_i < _src.Length && IsIdentPart(Peek())) sb.Append(Next());
        return sb.ToString();
    }

    private Token LexString(SourcePos pos, int start)
    {
        Next(); // opening quote
        var parts = new List<StringPart>();
        var text = new StringBuilder();
        var raw = new StringBuilder();
        while (true)
        {
            if (_i >= _src.Length) throw new DslException(L.T("Lezáratlan szöveg (hiányzó \").", "Unterminated text (missing \")."), pos, start);
            var c = Next();
            if (c == '"') break;
            if (c == '\\')
            {
                if (_i >= _src.Length) throw new DslException(L.T("Lezáratlan szöveg.", "Unterminated text."), pos, start);
                var e = Next();
                switch (e)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': text.Append('\t'); break;
                    case 'r': text.Append('\r'); break;
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '{': text.Append('{'); break;
                    case '}': text.Append('}'); break;
                    case 'u':
                        var hex = "";
                        for (var k = 0; k < 4 && _i < _src.Length; k++) hex += Next();
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw new DslException(L.T($"Hibás \\u escape: \\u{hex}", $"Invalid \\u escape: \\u{hex}"), Pos, _i);
                        text.Append((char)code);
                        break;
                    default:
                        throw new DslException(L.T($"Ismeretlen escape: \\{e}", $"Unknown escape: \\{e}"), Pos, _i - 2, 2);
                }
                continue;
            }
            if (c == '{')
            {
                if (text.Length > 0) { parts.Add(new TextPart(text.ToString())); text.Clear(); }
                var ipos = Pos;
                var inner = new Lexer(_src, _i, _line, _col);
                var innerTokens = inner.LexUntil('}');
                _i = inner._i; _line = inner._line; _col = inner._col;
                parts.Add(new InterpolationPart(innerTokens, ipos));
                continue;
            }
            text.Append(c);
        }
        if (text.Length > 0 || parts.Count == 0) parts.Add(new TextPart(text.ToString()));
        return new Token(TokenKind.String, _src[start.._i], pos, start, _i - start) { Parts = parts };
    }
}
