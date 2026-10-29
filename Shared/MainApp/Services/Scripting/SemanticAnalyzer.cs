using System.Collections.Generic;
using System.Linq;

namespace FRAMLab.Shared.MainApp.Services.Scripting
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
            "SetOutput", "Cycle", "IsActive", "Print", "Trace", "API"
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
            CheckInitialization(program);
            return diagnostics;
        }

        // ---- definite-assignment analysis ----

        private HashSet<string> initialized = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Walks the program in order and reports scalar variables that are read before any
        /// assignment, unless their declaration supplies an initial value. Arrays always exist
        /// (they start empty and grow on write), so only their elements are checked at runtime.
        /// </summary>
        private void CheckInitialization(ProgramNode program)
        {
            initialized = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in symbols.All)
            {
                // Aspects and built-ins are supplied by the runtime; writable scalars are
                // initialized only when they declare an initial value.
                if (!s.IsWritable || s.HasInitialValue || s.IsArray)
                {
                    initialized.Add(s.Name);
                }
            }
            AnalyzeStatements(program.Statements, initialized);
        }

        private void AnalyzeStatements(IEnumerable<Statement> statements, HashSet<string> state)
        {
            foreach (var stmt in statements) AnalyzeStatement(stmt, state);
        }

        private static HashSet<string> Clone(HashSet<string> state)
            => new(state, System.StringComparer.OrdinalIgnoreCase);

        private void AnalyzeStatement(Statement stmt, HashSet<string> state)
        {
            switch (stmt)
            {
                case Assignment a:
                    AnalyzeExpression(a.Index, state);
                    AnalyzeExpression(a.Index2, state);
                    AnalyzeExpression(a.Value, state);
                    if (a.Index is null)
                    {
                        state.Add(a.Target);
                    }
                    break;

                case IfStatement i:
                    AnalyzeExpression(i.Condition, state);
                    var branches = new List<HashSet<string>>();
                    var thenState = Clone(state);
                    AnalyzeStatements(i.ThenBlock.Statements, thenState);
                    branches.Add(thenState);
                    foreach (var (cond, body) in i.ElsIfBranches)
                    {
                        AnalyzeExpression(cond, state);
                        var elsifState = Clone(state);
                        AnalyzeStatements(body.Statements, elsifState);
                        branches.Add(elsifState);
                    }
                    if (i.ElseBlock is not null)
                    {
                        var elseState = Clone(state);
                        AnalyzeStatements(i.ElseBlock.Statements, elseState);
                        branches.Add(elseState);
                    }
                    else
                    {
                        branches.Add(Clone(state));
                    }
                    var merged = branches[0];
                    for (var b = 1; b < branches.Count; b++) merged.IntersectWith(branches[b]);
                    state.Clear();
                    foreach (var name in merged) state.Add(name);
                    break;

                case ForStatement f:
                    AnalyzeExpression(f.From, state);
                    AnalyzeExpression(f.To, state);
                    if (f.By is not null) AnalyzeExpression(f.By, state);
                    var bodyState = Clone(state);
                    bodyState.Add(f.Variable);
                    AnalyzeStatements(f.Body.Statements, bodyState);
                    state.Add(f.Variable); // FOR assigns its control variable
                    break;

                case WhileStatement w:
                    AnalyzeExpression(w.Condition, state);
                    var whileState = Clone(state);
                    AnalyzeStatements(w.Body.Statements, whileState);
                    // The loop may run zero times, so only the entry state is guaranteed.
                    break;

                case RepeatStatement r:
                    var repeatState = Clone(state);
                    AnalyzeStatements(r.Body.Statements, repeatState);
                    AnalyzeExpression(r.Until, repeatState);
                    // REPEAT runs its body at least once.
                    state.Clear();
                    foreach (var name in repeatState) state.Add(name);
                    break;

                case CaseStatement cs:
                    // like IF: a variable is assigned after the CASE only if every path assigns it
                    AnalyzeExpression(cs.Selector, state);
                    var caseBranches = new List<HashSet<string>>();
                    foreach (var branch in cs.Branches)
                    {
                        var branchState = Clone(state);
                        AnalyzeStatements(branch.Body.Statements, branchState);
                        caseBranches.Add(branchState);
                    }
                    if (cs.ElseBlock is not null)
                    {
                        var elseState = Clone(state);
                        AnalyzeStatements(cs.ElseBlock.Statements, elseState);
                        caseBranches.Add(elseState);
                    }
                    else
                    {
                        caseBranches.Add(Clone(state));
                    }
                    var caseMerged = caseBranches[0];
                    for (var b = 1; b < caseBranches.Count; b++) caseMerged.IntersectWith(caseBranches[b]);
                    state.Clear();
                    foreach (var name in caseMerged) state.Add(name);
                    break;

                case ReturnStatement ret:
                    if (ret.Value is not null) AnalyzeExpression(ret.Value, state);
                    break;

                case ExprStatement es:
                    AnalyzeExpression(es.Expr, state);
                    break;
            }
        }

        private void AnalyzeExpression(Expression? expr, HashSet<string> state)
        {
            switch (expr)
            {
                case null:
                    return;
                case IdentifierExpr id:
                    CheckRead(id, state);
                    break;
                case IndexExpr ix:
                    if (ix.Target is IdentifierExpr indexed)
                    {
                        CheckRead(indexed, state);
                    }
                    else
                    {
                        AnalyzeExpression(ix.Target, state);
                    }
                    AnalyzeExpression(ix.Index, state);
                    AnalyzeExpression(ix.Index2, state);
                    break;
                case UnaryExpr u:
                    AnalyzeExpression(u.Operand, state);
                    break;
                case BinaryExpr b:
                    AnalyzeExpression(b.Left, state);
                    AnalyzeExpression(b.Right, state);
                    break;
                case CallExpr c:
                    foreach (var argument in c.Arguments) AnalyzeExpression(argument, state);
                    break;
                case ArrayLiteralExpr al:
                    foreach (var element in al.Elements) AnalyzeExpression(element, state);
                    break;
            }
        }

        private void CheckRead(IdentifierExpr id, HashSet<string> state)
        {
            if (!symbols.TryGet(id.Name, out var sym) || sym is null) return;
            if (!sym.IsWritable || sym.HasInitialValue || sym.IsArray || state.Contains(id.Name)) return;
            Error(id, $"Variable '{id.Name}' is used before it is initialized. Assign a value before reading it, or provide an initial value in the IDE.");
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
                case CaseStatement cs:
                    VisitExpression(cs.Selector);
                    var selectorType = TypeOf(cs.Selector);
                    if (selectorType is not null && selectorType is not ("INT" or "ANY"))
                    {
                        Warn(cs.Selector, $"CASE selector should be INT, but is {selectorType}; its value is truncated to a whole number.");
                    }
                    var seen = new List<(long From, long To)>();
                    foreach (var branch in cs.Branches)
                    {
                        foreach (var label in branch.Labels)
                        {
                            var clash = seen.FindIndex(s => label.From <= s.To && s.From <= label.To);
                            if (clash >= 0)
                            {
                                static string Text((long From, long To) l) => l.From == l.To ? $"{l.From}" : $"{l.From}..{l.To}";
                                Error(branch, $"CASE label {Text(label)} is already covered by {Text(seen[clash])} in an earlier branch.");
                            }
                            seen.Add(label);
                        }
                        foreach (var s in branch.Body.Statements) VisitStatement(s);
                    }
                    if (cs.ElseBlock is not null) foreach (var s in cs.ElseBlock.Statements) VisitStatement(s);
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
            if (!symbols.TryGet(a.Target, out var sym) || sym is null)
            {
                Error(a, $"'{a.Target}' is not declared. Declare it in the variable table first.");
                return;
            }
            if (!sym.IsWritable)
            {
                Error(a, $"'{a.Target}' is a read-only {sym.Category} variable and cannot be assigned.");
            }

            if (a.Index is not null)
            {
                VisitExpression(a.Index);
                if (!sym.IsArray)
                {
                    Error(a, $"'{a.Target}' is a {sym.DataType} variable, not an array; tick the Array checkbox for it in the variable table.");
                    return;
                }
                var indexType = TypeOf(a.Index);
                if (indexType is not null && indexType is not ("INT" or "REAL" or "ANY"))
                {
                    Warn(a.Index, $"Array index should be INT, but is {indexType}.");
                }
                if (a.Index2 is not null) VisitExpression(a.Index2);
                if (!CheckIndexCount(a, a.Target, sym, a.Index2 is not null)) return;
                CheckConstantIndex(a.Index, a.Target);
                if (a.Index2 is not null) CheckConstantIndex(a.Index2, a.Target);
                if (IsArrayExpression(a.Value))
                {
                    Error(a.Value, $"Cannot assign an array to element '{a.Target}[...]'.");
                    return;
                }
                var elementValueType = TypeOf(a.Value);
                if (elementValueType is not null && sym.DataType != "ANY" && !IsAssignable(sym.DataType, elementValueType))
                {
                    Warn(a, $"Type mismatch: cannot assign {elementValueType} to element type {sym.DataType} of '{a.Target}'.");
                }
                return;
            }

            if (sym.IsArray)
            {
                if (!IsArrayExpression(a.Value))
                {
                    Error(a, sym.Rank == 2
                        ? $"'{a.Target}' is a 2-D array of {sym.DataType}; assign a table such as [[1, 2], [3, 4]], another table, or write one cell with '{a.Target}[row, col] := ...'."
                        : $"'{a.Target}' is an array of {sym.DataType}; assign an array literal such as [1, 2, 3], another array, or write one element with '{a.Target}[i] := ...'.");
                }
                else if (a.Value is ArrayLiteralExpr literal && literal.Elements.Count > 0)
                {
                    var isTable = literal.Elements.All(e => e is ArrayLiteralExpr);
                    if (sym.Rank == 2 && !isTable)
                    {
                        Error(a.Value, $"'{a.Target}' is a 2-D array; write the value as rows, e.g. [[1, 2], [3, 4]].");
                    }
                    else if (sym.Rank == 1 && isTable)
                    {
                        Error(a.Value, $"'{a.Target}' is a 1-D array; a table [[...], [...]] needs a 2-D array (choose 2-D next to the Array checkbox).");
                    }
                }
                else if (a.Value is IdentifierExpr source && symbols.TryGet(source.Name, out var sourceSym)
                    && sourceSym is { IsArray: true } && sourceSym.Rank != sym.Rank)
                {
                    Error(a.Value, $"'{source.Name}' is a {sourceSym.Rank}-D array but '{a.Target}' is {sym.Rank}-D.");
                }
                return;
            }

            if (IsArrayExpression(a.Value))
            {
                Error(a.Value, $"Cannot assign an array to scalar variable '{a.Target}'.");
                return;
            }
            var valueType = TypeOf(a.Value);
            if (valueType is not null && sym.DataType != "ANY" && !IsAssignable(sym.DataType, valueType))
            {
                Warn(a, $"Type mismatch: cannot assign {valueType} to {sym.DataType} variable '{a.Target}'.");
            }
        }

        /// <summary>A 1-D array takes one index (a[i]), a 2-D array two (m[row, col]).</summary>
        private bool CheckIndexCount(AstNode node, string name, Symbol sym, bool twoIndexes)
        {
            if (sym.Rank == 2 && !twoIndexes)
            {
                Error(node, $"'{name}' is a 2-D array; write {name}[row, column].");
                return false;
            }
            if (sym.Rank == 1 && twoIndexes)
            {
                Error(node, $"'{name}' is a 1-D array; write {name}[index], or choose 2-D for it in the variable table.");
                return false;
            }
            return true;
        }

        /// <summary>Arrays start at index 0 and grow on write, up to <see cref="STArray.MaxLength"/> elements.</summary>
        private void CheckConstantIndex(Expression index, string arrayName)
        {
            long? constant = index switch
            {
                Literal { LiteralType: "INT" } lit => (long)lit.Value!,
                UnaryExpr { Op: "-", Operand: Literal { LiteralType: "INT" } neg } => -(long)neg.Value!, // -1 parses as -(1)
                _ => null,
            };
            if (constant is long i)
            {
                if (!STArray.IsValidIndex(i))
                {
                    Error(index, $"Index {i} is outside '{arrayName}': indexes run from 0 to {STArray.MaxLength - 1}.");
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
                    if (ix.Index2 is not null) VisitExpression(ix.Index2);
                    if (ix.Target is IdentifierExpr indexed && symbols.TryGet(indexed.Name, out var indexedSym)
                        && indexedSym is not null)
                    {
                        if (!indexedSym.IsArray)
                        {
                            Error(ix, $"'{indexed.Name}' is not an array and cannot be indexed.");
                        }
                        else if (CheckIndexCount(ix, indexed.Name, indexedSym, ix.Index2 is not null))
                        {
                            CheckConstantIndex(ix.Index, indexed.Name);
                            if (ix.Index2 is not null) CheckConstantIndex(ix.Index2, indexed.Name);
                        }
                    }
                    break;
                case ArrayLiteralExpr al:
                    foreach (var element in al.Elements) VisitExpression(element);
                    if (al.Elements.Count > 0) // [] is allowed: it empties an array
                    {
                        var types = al.Elements.Select(TypeOf).Where(t => t is not null).Distinct().ToList();
                        if (types.Count > 1)
                        {
                            Warn(al, $"Array literal mixes element types ({string.Join(", ", types)}); values are converted to the target element type.");
                        }
                        // [[1, 2], [3, 4]] is a table: every element a row, and rows hold no further arrays
                        var rowCount = al.Elements.Count(e => e is ArrayLiteralExpr);
                        if (rowCount > 0 && rowCount < al.Elements.Count)
                        {
                            Error(al, "A table literal must contain only rows, e.g. [[1, 2], [3, 4]].");
                        }
                        foreach (var row in al.Elements.OfType<ArrayLiteralExpr>())
                        {
                            if (row.Elements.Any(IsArrayExpression)) Error(row, "Arrays have at most two dimensions.");
                        }
                        foreach (var element in al.Elements.Where(e => e is not ArrayLiteralExpr))
                        {
                            if (IsArrayExpression(element)) Error(element, "Array variables cannot be nested inside an array literal.");
                        }
                    }
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

        /// <summary>True when the expression denotes a whole array (literal or array variable).</summary>
        private bool IsArrayExpression(Expression expr) => expr switch
        {
            ArrayLiteralExpr => true,
            IdentifierExpr id => symbols.TryGet(id.Name, out var s) && s is not null && s.IsArray,
            _ => false,
        };

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
            // Host names are case-insensitive (hostFunctions uses OrdinalIgnoreCase).
            var upper = name.ToUpperInvariant();
            // Print/Trace are variadic: they accept one or more values and concatenate them.
            if (upper is "PRINT" or "TRACE")
            {
                if (c.Arguments.Count < 1)
                {
                    Error(c, $"'{name}' expects at least 1 argument.");
                }
            }
            else
            {
                int expected = upper switch
                {
                    "SETOUTPUT" or "API" => 2,
                    "GETINPUT" or "GETPRECONDITION" or "GETRESOURCE" or "GETCONTROL" or "GETTIME" => 1,
                    _ => 0, // Cycle, IsActive
                };
                if (c.Arguments.Count != expected)
                {
                    Error(c, $"'{name}' expects {expected} argument(s), got {c.Arguments.Count}.");
                }
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
            IndexExpr ix => ix.Target is IdentifierExpr id && symbols.TryGet(id.Name, out var s) ? s?.DataType : null,
            ArrayLiteralExpr al => al.Elements.Count == 0 ? null : al.Elements.Select(TypeOf).FirstOrDefault(t => t is not null),
            UnaryExpr u => u.Op == "NOT" ? "BOOL" : TypeOf(u.Operand),
            BinaryExpr b => b.Op switch
            {
                "AND" or "OR" or "XOR" => "BOOL",
                "=" or "<>" or "<" or "<=" or ">" or ">=" => "BOOL",
                "+" => (TypeOf(b.Left) == "STRING" || TypeOf(b.Right) == "STRING") ? "STRING" : NumericResult(TypeOf(b.Left), TypeOf(b.Right)),
                _ => NumericResult(TypeOf(b.Left), TypeOf(b.Right)),
            },
            CallExpr c => c.FunctionName.ToUpperInvariant() switch
            {
                "ISACTIVE" => "BOOL",
                "CYCLE" => "INT",
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
