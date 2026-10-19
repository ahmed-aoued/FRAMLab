using System.Collections.Generic;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    // ---- AST node hierarchy for the ST dialect ----
    public abstract class AstNode
    {
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public abstract class Statement : AstNode { }
    public abstract class Expression : AstNode { }

    public sealed class ProgramNode : AstNode
    {
        public List<Statement> Statements { get; } = new();
    }

    public sealed class Block : Statement
    {
        public List<Statement> Statements { get; } = new();
    }

    public sealed class Assignment : Statement
    {
        public string Target { get; set; } = "";
        public Expression? Index { get; set; }   // for a[i] := ... (array phase)
        public Expression Value { get; set; } = null!;
    }

    public sealed class IfStatement : Statement
    {
        public Expression Condition { get; set; } = null!;
        public Block ThenBlock { get; set; } = new();
        public List<(Expression Condition, Block Body)> ElsIfBranches { get; } = new();
        public Block? ElseBlock { get; set; }
    }

    public sealed class ForStatement : Statement
    {
        public string Variable { get; set; } = "";
        public Expression From { get; set; } = null!;
        public Expression To { get; set; } = null!;
        public Expression? By { get; set; }
        public Block Body { get; set; } = new();
    }

    public sealed class WhileStatement : Statement
    {
        public Expression Condition { get; set; } = null!;
        public Block Body { get; set; } = new();
    }

    public sealed class RepeatStatement : Statement
    {
        public Block Body { get; set; } = new();
        public Expression Until { get; set; } = null!;
    }

    public sealed class ReturnStatement : Statement { public Expression? Value { get; set; } }
    public sealed class ExitStatement : Statement { }
    public sealed class ExprStatement : Statement { public Expression Expr { get; set; } = null!; }

    // ---- expressions ----
    public sealed class Literal : Expression
    {
        public object? Value { get; set; }   // bool, long, double, string, TimeSpan
        public string LiteralType { get; set; } = ""; // BOOL/INT/REAL/STRING/TIME
    }

    public sealed class IdentifierExpr : Expression
    {
        public string Name { get; set; } = "";
    }

    public sealed class BinaryExpr : Expression
    {
        public string Op { get; set; } = "";
        public Expression Left { get; set; } = null!;
        public Expression Right { get; set; } = null!;
    }

    public sealed class UnaryExpr : Expression
    {
        public string Op { get; set; } = "";
        public Expression Operand { get; set; } = null!;
    }

    public sealed class CallExpr : Expression
    {
        public string FunctionName { get; set; } = "";
        public List<Expression> Arguments { get; } = new();
    }

    public sealed class IndexExpr : Expression
    {
        public Expression Target { get; set; } = null!;
        public Expression Index { get; set; } = null!;
    }
}
