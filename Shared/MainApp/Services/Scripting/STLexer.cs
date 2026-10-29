using System.Globalization;
using System.Text;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>Lexical analyzer for the ST dialect. Produces tokens with 1-based line/col.</summary>
    public sealed class STLexer
    {
        private readonly string src;
        private int pos = 0;
        private int line = 1;
        private int col = 1;
        private readonly List<ScriptDiagnostic> errors = new();

        public STLexer(string source)
        {
            src = source ?? "";
        }

        public IReadOnlyList<ScriptDiagnostic> Errors => errors;

        private char Peek(int ahead = 0) => (pos + ahead < src.Length) ? src[pos + ahead] : '\0';
        private string PeekText(int length) => src.Substring(pos, Math.Min(length, src.Length - pos));

        private char Advance()
        {
            var c = Peek();
            pos++;
            if (c == '\n') { line++; col = 1; } else { col++; }
            return c;
        }

        private static readonly Dictionary<string, TokenKind> keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["IF"] = TokenKind.If, ["THEN"] = TokenKind.Then, ["ELSIF"] = TokenKind.ElsIf,
            ["ELSE"] = TokenKind.Else, ["END_IF"] = TokenKind.EndIf,
            ["FOR"] = TokenKind.For, ["TO"] = TokenKind.To, ["BY"] = TokenKind.By,
            ["DO"] = TokenKind.Do, ["END_FOR"] = TokenKind.EndFor,
            ["WHILE"] = TokenKind.While, ["END_WHILE"] = TokenKind.EndWhile,
            ["REPEAT"] = TokenKind.Repeat, ["UNTIL"] = TokenKind.Until, ["END_REPEAT"] = TokenKind.EndRepeat,
            ["CASE"] = TokenKind.Case, ["OF"] = TokenKind.Of, ["END_CASE"] = TokenKind.EndCase,
            ["VAR"] = TokenKind.Var, ["END_VAR"] = TokenKind.EndVar,
            ["RETURN"] = TokenKind.Return, ["EXIT"] = TokenKind.Exit,
            ["AND"] = TokenKind.And, ["OR"] = TokenKind.Or, ["XOR"] = TokenKind.Xor,
            ["NOT"] = TokenKind.Not, ["MOD"] = TokenKind.Mod,
            ["TRUE"] = TokenKind.True, ["FALSE"] = TokenKind.False,
            ["BOOL"] = TokenKind.TypeBool, ["INT"] = TokenKind.TypeInt, ["REAL"] = TokenKind.TypeReal,
            ["STRING"] = TokenKind.TypeString, ["TIME"] = TokenKind.TypeTime,
        };

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            while (true)
            {
                var t = Next();
                tokens.Add(t);
                if (t.Kind == TokenKind.EndOfFile) break;
            }
            return tokens;
        }

        private Token Next()
        {
            // skip whitespace and comments
            while (true)
            {
                var c = Peek();
                if (char.IsWhiteSpace(c)) { Advance(); continue; }
                // block comment (* ... *)
                if (c == '(' && Peek(1) == '*')
                {
                    var startLine = line; var startCol = col;
                    Advance(); Advance();
                    var closed = false;
                    while (pos < src.Length)
                    {
                        if (Peek() == '*' && Peek(1) == ')') { Advance(); Advance(); closed = true; break; }
                        Advance();
                    }
                    if (!closed)
                    {
                        errors.Add(new ScriptDiagnostic("Error", "Unterminated comment", startLine, startCol, line, col));
                    }
                    continue;
                }
                // line comment //
                if (c == '/' && Peek(1) == '/')
                {
                    while (pos < src.Length && Peek() != '\n') Advance();
                    continue;
                }
                break;
            }

            var line0 = line; var col0 = col;
            var ch = Peek();
            if (ch == '\0') return new Token(TokenKind.EndOfFile, "", line0, col0, 0);

            // time literals T#... / TIME#... (checked before identifiers, which would
            // otherwise consume the leading T and leave a stray '#')
            if ((ch == 'T' || ch == 't') && Peek(1) == '#')
            {
                return LexTime(line0, col0, 1);
            }
            if ((ch == 'T' || ch == 't') && string.Equals(PeekText(5), "TIME#", StringComparison.OrdinalIgnoreCase))
            {
                return LexTime(line0, col0, 4);
            }

            // identifiers / keywords
            if (char.IsLetter(ch) || ch == '_')
            {
                var sb = new StringBuilder();
                while (char.IsLetterOrDigit(Peek()) || Peek() == '_') sb.Append(Advance());
                var text = sb.ToString();
                var kind = keywords.TryGetValue(text, out var k) ? k : TokenKind.Identifier;
                if (kind == TokenKind.True || kind == TokenKind.False) kind = TokenKind.BoolLiteral;
                return new Token(kind, text, line0, col0, text.Length);
            }

            // numbers (int / real)
            if (char.IsDigit(ch))
            {
                return LexNumber(line0, col0);
            }

            // string literal '...' (IEC) or "..." (accepted for convenience)
            if (ch == '\'' || ch == '"')
            {
                return LexString(line0, col0, ch);
            }

            // two-char operators
            var two = new string(new[] { ch, Peek(1) });
            switch (two)
            {
                case ":=": Advance(); Advance(); return new Token(TokenKind.Assign, two, line0, col0, 2);
                case "<>": Advance(); Advance(); return new Token(TokenKind.Ne, two, line0, col0, 2);
                case "<=": Advance(); Advance(); return new Token(TokenKind.Le, two, line0, col0, 2);
                case ">=": Advance(); Advance(); return new Token(TokenKind.Ge, two, line0, col0, 2);
                case "..": Advance(); Advance(); return new Token(TokenKind.DotDot, two, line0, col0, 2);
            }

            // single-char operators
            Advance();
            return ch switch
            {
                '+' => new Token(TokenKind.Plus, "+", line0, col0, 1),
                '-' => new Token(TokenKind.Minus, "-", line0, col0, 1),
                '*' => new Token(TokenKind.Star, "*", line0, col0, 1),
                '/' => new Token(TokenKind.Slash, "/", line0, col0, 1),
                '(' => new Token(TokenKind.LParen, "(", line0, col0, 1),
                ')' => new Token(TokenKind.RParen, ")", line0, col0, 1),
                '[' => new Token(TokenKind.LBracket, "[", line0, col0, 1),
                ']' => new Token(TokenKind.RBracket, "]", line0, col0, 1),
                ';' => new Token(TokenKind.Semicolon, ";", line0, col0, 1),
                ',' => new Token(TokenKind.Comma, ",", line0, col0, 1),
                ':' => new Token(TokenKind.Colon, ":", line0, col0, 1),
                '=' => new Token(TokenKind.Eq, "=", line0, col0, 1),
                '<' => new Token(TokenKind.Lt, "<", line0, col0, 1),
                '>' => new Token(TokenKind.Gt, ">", line0, col0, 1),
                _ => ErrorToken(line0, col0, ch),
            };
        }

        private Token ErrorToken(int line0, int col0, char ch)
        {
            errors.Add(new ScriptDiagnostic("Error", $"Unexpected character '{ch}'", line0, col0, line0, col0 + 1));
            return new Token(TokenKind.EndOfFile, ch.ToString(), line0, col0, 1);
        }

        private Token LexNumber(int line0, int col0)
        {
            var sb = new StringBuilder();
            var isReal = false;
            while (char.IsDigit(Peek())) sb.Append(Advance());
            if (Peek() == '.' && char.IsDigit(Peek(1)))
            {
                isReal = true;
                sb.Append(Advance()); // .
                while (char.IsDigit(Peek())) sb.Append(Advance());
            }
            if (Peek() == 'e' || Peek() == 'E')
            {
                isReal = true;
                sb.Append(Advance());
                if (Peek() == '+' || Peek() == '-') sb.Append(Advance());
                while (char.IsDigit(Peek())) sb.Append(Advance());
            }
            var text = sb.ToString();
            return new Token(isReal ? TokenKind.RealLiteral : TokenKind.IntLiteral, text, line0, col0, text.Length);
        }

        private Token LexTime(int line0, int col0, int prefixLength)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < prefixLength; i++) sb.Append(Advance()); // T or TIME
            sb.Append(Advance()); // #
            if (Peek() == '-') sb.Append(Advance());
            while (char.IsDigit(Peek()) || Peek() == '.') sb.Append(Advance());
            // unit ms|s|m|h|d
            var unitStart = pos;
            while (char.IsLetter(Peek())) sb.Append(Advance());
            var text = sb.ToString();
            return new Token(TokenKind.TimeLiteral, text, line0, col0, text.Length);
        }

        private Token LexString(int line0, int col0, char quote)
        {
            Advance(); // opening quote
            var sb = new StringBuilder();
            while (pos < src.Length && Peek() != quote)
            {
                sb.Append(Advance());
            }
            if (Peek() == quote) Advance(); // closing quote
            else errors.Add(new ScriptDiagnostic("Error", "Unterminated string literal", line0, col0, line, col));
            var inner = sb.ToString();
            return new Token(TokenKind.StringLiteral, inner, line0, col0, inner.Length + 2);
        }
    }
}
