using System.Collections.Generic;

namespace FMV_OG.Shared.MainApp.Services.Scripting
{
    /// <summary>Static semantic analysis: resolves identifiers against the symbol table,
    /// checks assignment writability and basic type compatibility, verifies host-function
    /// calls, and produces diagnostics (does not execute).</summary>
    public sealed class SemanticAnalyzer
    {
        private readonly SymbolTable symbols;
        private readonly List<ScriptDiagnostic> diagnostics = new();

        private static readonly HashSet<string> hostFunctions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "GetInput", "GetPrecondition", "GetResource", "GetControl", "GetTime",
            "SetOutput", "Cycle", "IsActive", "Print", "Trace"
        };
        private static readonly HashSet<string> builtinFunctions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "ABS", "SQRT", "SIN", "COS", "TAN", "LN", "EXP", "MIN", "MAX", "LIMIT", "ROUND", "TRUNC"
        };

        public SemanticAnalyzer(SymbolTable symbols)
        {
            this.symbols = symbols;
        }

        public IReadOnlyList<ScriptDiagnostic> Analyze(ProgramNode program)
        {
            foreach (var stmt in program.Statements) VisitStatement(stmt);
            return diagnostics;
        }

        private void Error(AstNode n, string msg) =>
            diagnostics.Add(new ScriptDiagnostic("Error", msg, n.Line, n.Column, n.Line, n.Column + 1));
        private void Warn(AstNode n, string msg) =>
            diagnostics.Add(new ScriptDiagnostic("Warning", msg, n.Line, n.Column, n.Line, n.Column + 1));

        private void VisitStatement(Statement stmt)
        {
            switch (stmt)
            {
                case Assignment a:
                    VisitAssignment(a);
                    break;
                case IfStatement i:
                    VisitExpression(i.Condition);
                    ExpectBool(i.Condition, "IF");
                    foreach (var s in i.ThenBlock.Statements) VisitStatement(s);
                    foreach (var (cond, body) in i.ElsIfBranches)
                    {
                        VisitExpression(cond);
                        ExpectBool(cond, "ELSIF");
                        foreach (var s in body.Statements) VisitStatement(s);
                    }
                    if (i.ElseBlock is not null) foreach (var s in i.ElseBlock.Statements) VisitStatement(s);
                    break;
                case ForStatement f:
                    VisitExpression(f.From); VisitExpression(f.To);
                    if (f.By is not null) VisitExpression(f.By);
                    if (!symbols.TryGet(f.Variable, out var loopVar))
                    {
                        // FOR loop control variable is implicitly declared INT
                        symbols.Add(new Symbol(f.Variable, "INT", true, "Local"));
                    }
                    else if (loopVar is not null && !loopVar.IsWritable)
                    {
                        Error(f, $"'{f.Variable}' is read-only and cannot be used as a FOR loop variable.");
                    }
                    foreach (var s in f.Body.Statements) VisitStatement(s);
                    break;
                case WhileStatement w:
                    VisitExpression(w.Condition);
                    ExpectBool(w.Condition, "WHILE");
                    foreach (var s in w.Body.Statements) VisitStatement(s);
                    break;
                case RepeatStatement r:
                    foreach (var s in r.Body.Statements) VisitStatement(s);
                    VisitExpression(r.Until);
                    ExpectBool(r.Until, "UNTIL");
                    break;
                case ReturnStatement ret:
                    if (ret.Value is not null) VisitExpression(ret.Value);
                    break;
                case ExprStatement es:
                    VisitExpression(es.Expr);
                    break;
            }
        }

        private void VisitAssignment(Assignment a)
        {
            VisitExpression(a.Value);
            if (!symbols.TryGet(a.Target, out var sym))
            {
                Error(a, $"'{a.Target}' is not declared. Declare it in the variable table first.");
                return;
            }
            if (sym is not null && !sym.IsWritable)
            {
                Error(a, $"'{a.Target}' is a read-only {sym.Category} variable and cannot be assigned.");
            }
            if (sym is not null)
            {
                var valueType = TypeOf(a.Value);
                if (valueType is not null && sym.DataType != "ANY" && !IsAssignable(sym.DataType, valueType))
                {
                    Warn(a, $"Type mismatch: cannot assign {valueType} to {sym.DataType} variable '{a.Target}'.");
                }
            }
        }

        private void ExpectBool(Expression e, string context)
        {
            var t = TypeOf(e);
            if (t is not null && t != "BOOL")
            {
                Warn(e, $"{context} condition should be BOOL, but is {t}.");
            }
        }

        private string? VisitExpression(Expression expr)
        {
            switch (expr)
            {
                case BinaryExpr b:
                    VisitExpression(b.Left);
                    VisitExpression(b.Right);
                    CheckBinary(b);
                    break;
                case UnaryExpr u:
                    VisitExpression(u.Operand);
                    break;
                case CallExpr c:
                    VisitCall(c);
                    break;
                case IndexExpr ix:
                    VisitExpression(ix.Target);
                    VisitExpression(ix.Index);
                    break;
                case IdentifierExpr id:
                    if (!symbols.Contains(id.Name))
                    {
                        Error(id, $"'{id.Name}' is not declared.");
                    }
                    break;
            }
            return TypeOf(expr);
        }

        private void VisitCall(CallExpr c)
        {
            if (hostFunctions.Contains(c.FunctionName))
            {
                CheckHostCall(c);
            }
            else if (builtinFunctions.Contains(c.FunctionName))
            {
                foreach (var a in c.Arguments) VisitExpression(a);
            }
            else
            {
                Error(c, $"Unknown function '{c.FunctionName}'.");
                foreach (var a in c.Arguments) VisitExpression(a);
            }
        }

        private void CheckHostCall(CallExpr c)
        {
            var name = c.FunctionName;
            int expected = name switch
            {
                "SetOutput" => 2,
                "GetInput" or "GetPrecondition" or "GetResource" or "GetControl" or "GetTime" => 1,
                "Print" or "Trace" => 1,
                _ => 0,
            };
            if (name is "Cycle" or "IsActive") expected = 0;
            if (c.Arguments.Count != expected)
            {
                Error(c, $"'{name}' expects {expected} argument(s), got {c.Arguments.Count}.");
            }
            foreach (var a in c.Arguments) VisitExpression(a);
        }

        private void CheckBinary(BinaryExpr b)
        {
            var lt = TypeOf(b.Left);
            var rt = TypeOf(b.Right);
            switch (b.Op)
            {
                case "AND": case "OR": case "XOR":
                    if (lt is not null && lt != "BOOL") Warn(b.Left, $"Operand of {b.Op} should be BOOL, got {lt}.");
                    if (rt is not null && rt != "BOOL") Warn(b.Right, $"Operand of {b.Op} should be BOOL, got {rt}.");
                    break;
                case "+":
                    // allow string concat
                    break;
                case "-": case "*": case "/": case "MOD":
                    if (lt is not null && lt is "STRING" or "BOOL") Warn(b.Left, $"Left operand of '{b.Op}' should be numeric, got {lt}.");
                    if (rt is not null && rt is "STRING" or "BOOL") Warn(b.Right, $"Right operand of '{b.Op}' should be numeric, got {rt}.");
                    break;
            }
        }

        /// <summary>Best-effort static type of an expression; null when unknown (e.g. host getter).</summary>
        private string? TypeOf(Expression expr) => expr switch
        {
            Literal l => l.LiteralType,
            IdentifierExpr id => symbols.TryGet(id.Name, out var s) ? s?.DataType : null,
            UnaryExpr u => u.Op == "NOT" ? "BOOL" : TypeOf(u.Operand),
            BinaryExpr b => b.Op switch
            {
                "AND" or "OR" or "XOR" => "BOOL",
                "=" or "<>" or "<" or "<=" or ">" or ">=" => "BOOL",
                "+" => (TypeOf(b.Left) == "STRING" || TypeOf(b.Right) == "STRING") ? "STRING" : NumericResult(TypeOf(b.Left), TypeOf(b.Right)),
                _ => NumericResult(TypeOf(b.Left), TypeOf(b.Right)),
            },
            CallExpr c => c.FunctionName switch
            {
                "IsActive" => "BOOL",
                "Cycle" => "INT",
                "ABS" or "SQRT" or "SIN" or "COS" or "TAN" or "LN" or "EXP" => "REAL",
                "ROUND" or "TRUNC" => "INT",
                "MIN" or "MAX" or "LIMIT" => "REAL",
                _ => null, // Get*/SetOutput -> ANY/void
            },
            _ => null,
        };

        private static string NumericResult(string? l, string? r)
        {
            if (l == "REAL" || r == "REAL") return "REAL";
            if (l == "INT" || r == "INT") return "INT";
            return "REAL";
        }

        private static bool IsAssignable(string target, string source)
        {
            if (target == source) return true;
            if (target == "REAL" && source == "INT") return true;   // widening
            if (target == "ANY") return true;
            return false;
        }
    }
}
