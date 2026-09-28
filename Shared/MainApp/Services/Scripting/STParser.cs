using System;
using System.Collections.Generic;
using System.Globalization;

namespace FMV_OG.Shared.MainApp.Services.Scripting
{
    /// <summary>Recursive-descent parser producing an AST plus error diagnostics.
    /// Uses error recovery (synchronize on ';' and block terminators) so one bad
    /// statement doesn't abort the whole parse.</summary>
    public sealed class STParser
    {
        private readonly List<Token> tokens;
        private int pos = 0;
        private readonly List<ScriptDiagnostic> diagnostics = new();

        public STParser(List<Token> tokens)
        {
            this.tokens = tokens;
        }

        public IReadOnlyList<ScriptDiagnostic> Diagnostics => diagnostics;

        private Token Current => tokens[Math.Min(pos, tokens.Count - 1)];
        private Token Previous => tokens[Math.Max(0, pos - 1)];
        private bool IsAtEnd => Current.Kind == TokenKind.EndOfFile;
        private Token Advance() { if (!IsAtEnd) pos++; return Previous; }
        private bool Check(TokenKind k) => Current.Kind == k;
        private bool Match(params TokenKind[] kinds)
        {
            foreach (var k in kinds) if (Check(k)) { Advance(); return true; }
            return false;
        }
        private Token Consume(TokenKind k, string message)
        {
            if (Check(k)) return Advance();
            Error(Current, message);
            return new Token(k, "", Current.Line, Current.Column, 0); // synthetic
        }
        private void Error(Token t, string message) =>
            diagnostics.Add(new ScriptDiagnostic("Error", message, t.Line, t.Column, t.Line, t.Column + Math.Max(1, t.Length)));

        public ProgramNode ParseProgram()
        {
            var program = new ProgramNode { Line = 1, Column = 1 };
            while (!IsAtEnd)
            {
                var stmt = ParseStatement();
                if (stmt is not null) program.Statements.Add(stmt);
                else Synchronize();
            }
            return program;
        }

        private void Synchronize()
        {
            Advance();
            while (!IsAtEnd)
            {
                if (Previous.Kind == TokenKind.Semicolon) return;
                switch (Current.Kind)
                {
                    case TokenKind.If: case TokenKind.For: case TokenKind.While:
                    case TokenKind.Repeat: case TokenKind.Return: case TokenKind.Exit:
                    case TokenKind.EndIf: case TokenKind.EndFor: case TokenKind.EndWhile:
                    case TokenKind.EndRepeat: case TokenKind.Else: case TokenKind.ElsIf:
                        return;
                }
                Advance();
            }
        }

        private Statement? ParseStatement()
        {
            try
            {
                if (Match(TokenKind.If)) return ParseIf(Previous);
                if (Match(TokenKind.For)) return ParseFor(Previous);
                if (Match(TokenKind.While)) return ParseWhile(Previous);
                if (Match(TokenKind.Repeat)) return ParseRepeat(Previous);
                if (Match(TokenKind.Return)) return ParseReturn(Previous);
                if (Match(TokenKind.Exit)) { Consume(TokenKind.Semicolon, "Expected ';' after EXIT."); return new ExitStatement { Line = Previous.Line, Column = Previous.Column }; }
                if (Check(TokenKind.Identifier)) return ParseAssignmentOrExpr();
                // stray semicolons are tolerated
                if (Match(TokenKind.Semicolon)) return null;
                Error(Current, $"Unexpected token '{Current.Text}'.");
                return null;
            }
            catch (Exception)
            {
                return null; // error already recorded; recover
            }
        }

        private Statement ParseAssignmentOrExpr()
        {
            var ident = Advance();
            Expression? index = null;
            if (Match(TokenKind.LBracket))
            {
                index = ParseExpression();
                Consume(TokenKind.RBracket, "Expected ']' after index.");
            }
            if (Match(TokenKind.Assign))
            {
                var value = ParseExpression();
                Consume(TokenKind.Semicolon, "Expected ';' after assignment.");
                return new Assignment { Target = ident.Text, Index = index, Value = value, Line = ident.Line, Column = ident.Column };
            }
            // not an assignment -> expression statement (e.g. a function call used for side effects)
            pos--; // back up to re-parse as expression
            var expr = ParseExpression();
            Consume(TokenKind.Semicolon, "Expected ';' after expression.");
            return new ExprStatement { Expr = expr, Line = ident.Line, Column = ident.Column };
        }

        private Statement ParseIf(Token ifTok)
        {
            var node = new IfStatement { Line = ifTok.Line, Column = ifTok.Column };
            node.Condition = ParseExpression();
            Consume(TokenKind.Then, "Expected THEN after IF condition.");
            node.ThenBlock = ParseBlock(TokenKind.ElsIf, TokenKind.Else, TokenKind.EndIf);
            while (Match(TokenKind.ElsIf))
            {
                var cond = ParseExpression();
                Consume(TokenKind.Then, "Expected THEN after ELSIF condition.");
                var body = ParseBlock(TokenKind.ElsIf, TokenKind.Else, TokenKind.EndIf);
                node.ElsIfBranches.Add((cond, body));
            }
            if (Match(TokenKind.Else))
            {
                node.ElseBlock = ParseBlock(TokenKind.EndIf);
            }
            Consume(TokenKind.EndIf, "Expected END_IF.");
            Consume(TokenKind.Semicolon, "Expected ';' after END_IF.");
            return node;
        }

        private Statement ParseFor(Token forTok)
        {
            var node = new ForStatement { Line = forTok.Line, Column = forTok.Column };
            var ident = Consume(TokenKind.Identifier, "Expected loop variable after FOR.");
            node.Variable = ident.Text;
            Consume(TokenKind.Assign, "Expected ':=' after FOR variable.");
            node.From = ParseExpression();
            Consume(TokenKind.To, "Expected TO in FOR loop.");
            node.To = ParseExpression();
            if (Match(TokenKind.By)) node.By = ParseExpression();
            Consume(TokenKind.Do, "Expected DO in FOR loop.");
            node.Body = ParseBlock(TokenKind.EndFor);
            Consume(TokenKind.EndFor, "Expected END_FOR.");
            Consume(TokenKind.Semicolon, "Expected ';' after END_FOR.");
            return node;
        }

        private Statement ParseWhile(Token whileTok)
        {
            var node = new WhileStatement { Line = whileTok.Line, Column = whileTok.Column };
            node.Condition = ParseExpression();
            Consume(TokenKind.Do, "Expected DO in WHILE loop.");
            node.Body = ParseBlock(TokenKind.EndWhile);
            Consume(TokenKind.EndWhile, "Expected END_WHILE.");
            Consume(TokenKind.Semicolon, "Expected ';' after END_WHILE.");
            return node;
        }

        private Statement ParseRepeat(Token repTok)
        {
            var node = new RepeatStatement { Line = repTok.Line, Column = repTok.Column };
            node.Body = ParseBlock(TokenKind.Until);
            Consume(TokenKind.Until, "Expected UNTIL in REPEAT loop.");
            node.Until = ParseExpression();
            Consume(TokenKind.EndRepeat, "Expected END_REPEAT.");
            Consume(TokenKind.Semicolon, "Expected ';' after END_REPEAT.");
            return node;
        }

        private Statement ParseReturn(Token retTok)
        {
            var node = new ReturnStatement { Line = retTok.Line, Column = retTok.Column };
            if (!Check(TokenKind.Semicolon)) node.Value = ParseExpression();
            Consume(TokenKind.Semicolon, "Expected ';' after RETURN.");
            return node;
        }

        private Block ParseBlock(params TokenKind[] terminators)
        {
            var block = new Block { Line = Current.Line, Column = Current.Column };
            while (!IsAtEnd && Array.IndexOf(terminators, Current.Kind) < 0)
            {
                var stmt = ParseStatement();
                if (stmt is not null) block.Statements.Add(stmt);
                else Synchronize();
            }
            return block;
        }

        // ---- expression grammar (precedence climbing) ----
        public Expression ParseExpression() => ParseOr();

        private Expression ParseOr()
        {
            var left = ParseXor();
            while (Match(TokenKind.Or))
            {
                var op = Previous; var right = ParseXor();
                left = new BinaryExpr { Op = "OR", Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseXor()
        {
            var left = ParseAnd();
            while (Match(TokenKind.Xor))
            {
                var op = Previous; var right = ParseAnd();
                left = new BinaryExpr { Op = "XOR", Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseAnd()
        {
            var left = ParseEquality();
            while (Match(TokenKind.And))
            {
                var op = Previous; var right = ParseEquality();
                left = new BinaryExpr { Op = "AND", Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseEquality()
        {
            var left = ParseComparison();
            while (Match(TokenKind.Eq, TokenKind.Ne))
            {
                var op = Previous; var right = ParseComparison();
                left = new BinaryExpr { Op = op.Kind == TokenKind.Eq ? "=" : "<>", Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseComparison()
        {
            var left = ParseAdditive();
            while (Match(TokenKind.Lt, TokenKind.Le, TokenKind.Gt, TokenKind.Ge))
            {
                var op = Previous; var right = ParseAdditive();
                var opText = op.Kind switch { TokenKind.Lt => "<", TokenKind.Le => "<=", TokenKind.Gt => ">", _ => ">=" };
                left = new BinaryExpr { Op = opText, Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Match(TokenKind.Plus, TokenKind.Minus))
            {
                var op = Previous; var right = ParseMultiplicative();
                left = new BinaryExpr { Op = op.Kind == TokenKind.Plus ? "+" : "-", Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseMultiplicative()
        {
            var left = ParseUnary();
            while (Match(TokenKind.Star, TokenKind.Slash, TokenKind.Mod))
            {
                var op = Previous; var right = ParseUnary();
                var opText = op.Kind switch { TokenKind.Star => "*", TokenKind.Slash => "/", _ => "MOD" };
                left = new BinaryExpr { Op = opText, Left = left, Right = right, Line = op.Line, Column = op.Column };
            }
            return left;
        }
        private Expression ParseUnary()
        {
            if (Match(TokenKind.Not))
            {
                var op = Previous; var operand = ParseUnary();
                return new UnaryExpr { Op = "NOT", Operand = operand, Line = op.Line, Column = op.Column };
            }
            if (Match(TokenKind.Minus))
            {
                var op = Previous; var operand = ParseUnary();
                return new UnaryExpr { Op = "-", Operand = operand, Line = op.Line, Column = op.Column };
            }
            return ParsePrimary();
        }
        private Expression ParsePrimary()
        {
            var tok = Current;
            if (Match(TokenKind.BoolLiteral))
                return new Literal { Value = tok.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase), LiteralType = "BOOL", Line = tok.Line, Column = tok.Column };
            if (Match(TokenKind.IntLiteral))
                return new Literal { Value = long.Parse(tok.Text, CultureInfo.InvariantCulture), LiteralType = "INT", Line = tok.Line, Column = tok.Column };
            if (Match(TokenKind.RealLiteral))
                return new Literal { Value = double.Parse(tok.Text, CultureInfo.InvariantCulture), LiteralType = "REAL", Line = tok.Line, Column = tok.Column };
            if (Match(TokenKind.StringLiteral))
                return new Literal { Value = tok.Text, LiteralType = "STRING", Line = tok.Line, Column = tok.Column };
            if (Match(TokenKind.TimeLiteral))
                return new Literal { Value = ParseTimeLiteral(tok.Text), LiteralType = "TIME", Line = tok.Line, Column = tok.Column };
            if (Match(TokenKind.Identifier))
            {
                Expression expr = new IdentifierExpr { Name = tok.Text, Line = tok.Line, Column = tok.Column };
                if (Match(TokenKind.LParen))
                {
                    var call = new CallExpr { FunctionName = tok.Text, Line = tok.Line, Column = tok.Column };
                    if (!Check(TokenKind.RParen))
                    {
                        do { call.Arguments.Add(ParseExpression()); } while (Match(TokenKind.Comma));
                    }
                    Consume(TokenKind.RParen, "Expected ')' after arguments.");
                    expr = call;
                }
                else if (Match(TokenKind.LBracket))
                {
                    var idx = ParseExpression();
                    Consume(TokenKind.RBracket, "Expected ']' after index.");
                    expr = new IndexExpr { Target = expr, Index = idx, Line = tok.Line, Column = tok.Column };
                }
                return expr;
            }
            if (Match(TokenKind.LParen))
            {
                var inner = ParseExpression();
                Consume(TokenKind.RParen, "Expected ')' after expression.");
                return inner;
            }
            Error(tok, $"Expected an expression but found '{tok.Text}'.");
            Advance();
            return new Literal { Value = 0.0, LiteralType = "REAL", Line = tok.Line, Column = tok.Column }; // error recovery
        }

        private static TimeSpan ParseTimeLiteral(string text)
        {
            // forms: T#5s, T#100ms, T#2m, T#1h, T#1d, TIME#3s
            var t = text;
            var hash = t.IndexOf('#');
            if (hash >= 0) t = t.Substring(hash + 1);
            var negative = t.StartsWith("-");
            if (negative) t = t.Substring(1);
            var i = 0;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.')) i++;
            var numPart = t.Substring(0, i);
            var unit = t.Substring(i).ToLowerInvariant();
            if (!double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) num = 0;
            var ms = unit switch
            {
                "ms" => num,
                "s" or "" => num * 1000,
                "m" => num * 60_000,
                "h" => num * 3_600_000,
                "d" => num * 86_400_000,
                _ => num * 1000,
            };
            var span = TimeSpan.FromMilliseconds(ms);
            return negative ? -span : span;
        }
    }
}
