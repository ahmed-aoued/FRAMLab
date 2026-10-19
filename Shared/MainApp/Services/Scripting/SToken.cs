namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    public enum TokenKind
    {
        // literals / identifiers
        Identifier, IntLiteral, RealLiteral, StringLiteral, TimeLiteral,
        BoolLiteral,
        // keywords
        If, Then, ElsIf, Else, EndIf,
        For, To, By, Do, EndFor,
        While, EndWhile,
        Repeat, Until, EndRepeat,
        Case, Of, EndCase,
        Var, EndVar,
        Return, Exit,
        And, Or, Xor, Not, Mod,
        True, False,
        // types
        TypeBool, TypeInt, TypeReal, TypeString, TypeTime,
        // symbols
        Assign,      // :=
        Plus, Minus, Star, Slash,
        LParen, RParen, LBracket, RBracket,
        Semicolon, Comma, Colon, DotDot,
        Eq, Ne, Lt, Le, Gt, Ge,
        EndOfFile
    }

    public sealed class Token
    {
        public TokenKind Kind { get; }
        public string Text { get; }
        public int Line { get; }
        public int Column { get; }
        public int Length { get; }

        public Token(TokenKind kind, string text, int line, int column, int length)
        {
            Kind = kind; Text = text; Line = line; Column = column; Length = length;
        }

        public override string ToString() => $"{Kind} '{Text}' @ {Line}:{Column}";
    }
}
