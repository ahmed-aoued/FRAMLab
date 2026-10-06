using System;
using System.Collections.Generic;
using System.Linq;
using FMV_OG.Shared.MainApp.Services.Logging;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services.Scripting
{
    /// <summary>Runtime context handed to the interpreter each cycle: access to the
    /// owning function, the FMI cycle number, and the shared global store.</summary>
    public sealed class ScriptExecutionContext
    {
        public Function Function { get; }
        public int Cycle { get; }
        public Dictionary<string, STValue> Globals { get; }
        /// <summary>Runtime diagnostics (e.g. div-by-zero, guard trips) appended here.</summary>
        public List<ScriptDiagnostic> RuntimeIssues { get; } = new();
        /// <summary>Messages from Print/Trace, surfaced to the FMI log.</summary>
        public List<string> Trace { get; } = new();

        public ScriptExecutionContext(Function function, int cycle, Dictionary<string, STValue> globals)
        {
            Function = function; Cycle = cycle; Globals = globals;
        }
    }

    /// <summary>Thrown by the interpreter to unwind execution (RETURN/EXIT/guards).</summary>
    public sealed class ScriptSignal : Exception
    {
        public string Kind { get; } // "Return" | "Exit" | "Guard"
        public ScriptSignal(string kind) { Kind = kind; }
    }

    /// <summary>Tree-walking interpreter for the ST dialect. Executes a compiled AST
    /// against Temp/Static/Global scope stores with statement + loop guards.</summary>
    public sealed class STInterpreter
    {
        private readonly ScriptExecutionContext ctx;
        private readonly Dictionary<string, STValue> tempStore = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, STValue> staticStore;
        private readonly Dictionary<string, STValue> outputs = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Values of aspect variables read/written during this cycle's run.</summary>
        private readonly Dictionary<string, STValue> aspectCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger? logger;

        private long statementCount = 0;
        private const long MaxStatements = 100_000;
        private const int MaxLoopIterations = 100_000;

        public STInterpreter(ScriptExecutionContext ctx, Dictionary<string, STValue> staticStore, ILogger? logger = null)
        {
            this.ctx = ctx;
            this.staticStore = staticStore;
            this.logger = logger;
        }

        /// <summary>Values written via SetOutput or to Output-category variables, read after the run.</summary>
        public IReadOnlyDictionary<string, STValue> Outputs => outputs;

        public void Run(ProgramNode program, Models.ScriptModel model)
        {
            // init Temp variables from their initial values
            foreach (var v in model.Variables)
            {
                if (v.Scope == Models.ScriptScope.Temp)
                {
                    tempStore[v.Name] = InitialValueOf(v);
                }
            }
            try
            {
                ExecuteBlock(program.Statements);
            }
            catch (ScriptSignal)
            {
                // RETURN / EXIT / guard trip -> normal termination
            }
            // collect Output-category variables into the outputs map
            foreach (var v in model.Variables)
            {
                if (v.IsOutput && TryGet(v.Name, out var val))
                {
                    outputs[v.Name] = val;
                }
            }
        }

        private STValue InitialValueOf(Models.ScriptVariable v)
        {
            try
            {
                return v.Type switch
                {
                    Models.ScriptVarType.BOOL => STValue.FromBool(v.InitialValue is "TRUE" or "true" or "1"),
                    Models.ScriptVarType.INT => STValue.FromInt(long.Parse(v.InitialValue, System.Globalization.CultureInfo.InvariantCulture)),
                    Models.ScriptVarType.REAL => STValue.FromReal(double.Parse(v.InitialValue, System.Globalization.CultureInfo.InvariantCulture)),
                    Models.ScriptVarType.STRING => STValue.FromString(v.InitialValue),
                    Models.ScriptVarType.TIME => STValue.FromTime(TimeSpan.Zero),
                    _ => STValue.FromReal(0),
                };
            }
            catch (Exception ex)
            {
                logger?.LogWarning(LogEvents.ScriptInitialValue, ex,
                    "Invalid initial value '{InitialValue}' for {Scope} variable '{Name}' (type {Type}); defaulting to 0.",
                    v.InitialValue, v.Scope, v.Name, v.Type);
                return STValue.FromReal(0);
            }
        }

        private void Guard(AstNode node)
        {
            statementCount++;
            if (statementCount > MaxStatements)
            {
                ctx.RuntimeIssues.Add(ScriptDiagnostic.Error(
                    $"Execution aborted: exceeded {MaxStatements} statements (possible infinite loop).", node.Line, node.Column));
                throw new ScriptSignal("Guard");
            }
        }

        private void ExecuteBlock(IEnumerable<Statement> statements)
        {
            foreach (var stmt in statements) Execute(stmt);
        }

        private void Execute(Statement stmt)
        {
            Guard(stmt);
            switch (stmt)
            {
                case Assignment a:
                    var val = Eval(a.Value);
                    Set(a.Target, val, a);
                    break;
                case IfStatement i:
                    if (Eval(i.Condition).AsBool()) ExecuteBlock(i.ThenBlock.Statements);
                    else
                    {
                        var done = false;
                        foreach (var (cond, body) in i.ElsIfBranches)
                        {
                            if (Eval(cond).AsBool()) { ExecuteBlock(body.Statements); done = true; break; }
                        }
                        if (!done && i.ElseBlock is not null) ExecuteBlock(i.ElseBlock.Statements);
                    }
                    break;
                case ForStatement f:
                    ExecuteFor(f);
                    break;
                case WhileStatement w:
                    var wIter = 0;
                    while (Eval(w.Condition).AsBool())
                    {
                        if (++wIter > MaxLoopIterations) { LoopGuard(w); break; }
                        ExecuteBlock(w.Body.Statements);
                    }
                    break;
                case RepeatStatement r:
                    var rIter = 0;
                    do
                    {
                        if (++rIter > MaxLoopIterations) { LoopGuard(r); break; }
                        ExecuteBlock(r.Body.Statements);
                    } while (!Eval(r.Until).AsBool());
                    break;
                case ReturnStatement:
                    throw new ScriptSignal("Return");
                case ExitStatement:
                    throw new ScriptSignal("Exit");
                case ExprStatement es:
                    Eval(es.Expr);
                    break;
            }
        }

        private void LoopGuard(Statement stmt)
        {
            ctx.RuntimeIssues.Add(ScriptDiagnostic.Warning(
                $"Loop exceeded {MaxLoopIterations} iterations and was stopped.", stmt.Line, stmt.Column));
            throw new ScriptSignal("Guard");
        }

        private void ExecuteFor(ForStatement f)
        {
            var from = Eval(f.From).AsInt();
            var to = Eval(f.To).AsInt();
            var by = f.By is not null ? Eval(f.By).AsInt() : 1;
            if (by == 0) { ctx.RuntimeIssues.Add(ScriptDiagnostic.Error("FOR BY step cannot be 0.", f.Line, f.Column)); return; }
            tempStore[f.Variable] = STValue.FromInt(from);
            var iter = 0;
            if (by > 0)
            {
                for (var i = from; i <= to; i += by)
                {
                    if (++iter > MaxLoopIterations) { LoopGuard(f); break; }
                    tempStore[f.Variable] = STValue.FromInt(i);
                    try { ExecuteBlock(f.Body.Statements); }
                    catch (ScriptSignal s) when (s.Kind == "Exit") { break; }
                }
            }
            else
            {
                for (var i = from; i >= to; i += by)
                {
                    if (++iter > MaxLoopIterations) { LoopGuard(f); break; }
                    tempStore[f.Variable] = STValue.FromInt(i);
                    try { ExecuteBlock(f.Body.Statements); }
                    catch (ScriptSignal s) when (s.Kind == "Exit") { break; }
                }
            }
        }

        // ---- scope resolution: Temp > Static > Global > aspect variable ----
        private bool TryGet(string name, out STValue value)
        {
            if (tempStore.TryGetValue(name, out value)) return true;
            if (staticStore.TryGetValue(name, out value)) return true;
            if (ctx.Globals.TryGetValue(name, out value)) return true;
            if (TryGetAspect(name, out value)) return true;
            value = STValue.Void;
            return false;
        }

        /// <summary>Resolve an aspect variable from the function's FMI variables (best effort).</summary>
        private bool TryGetAspect(string name, out STValue value)
        {
            if (aspectCache.TryGetValue(name, out value)) return true;

            var aspect = FindAspect(name);
            if (aspect is null)
            {
                value = STValue.Void;
                return false;
            }

            // look for an FMI variable whose key ends with the aspect label, e.g. "I0_Hazard signal"
            var label = aspect.Label;
            foreach (var kv in ctx.Function.getVariables())
            {
                if (kv.Key.Equals($"Script_{label}", StringComparison.OrdinalIgnoreCase)
                    || kv.Key.EndsWith("_" + label, StringComparison.OrdinalIgnoreCase))
                {
                    var raw = kv.Value is { Length: > 0 } ? kv.Value[0] : "";
                    value = ParseValue(raw, aspect.Type);
                    aspectCache[name] = value;
                    return true;
                }
            }
            // no runtime value yet -> typed zero
            value = ZeroOf(aspect.Type);
            aspectCache[name] = value;
            return true;
        }

        private Models.AspectVariable? FindAspect(string name)
        {
            foreach (var av in ctx.Function.Script.AspectVariables)
            {
                if (string.Equals(av.Identifier, name, StringComparison.OrdinalIgnoreCase))
                {
                    return av;
                }
            }
            return null;
        }

        private static STValue ParseValue(string raw, Models.ScriptVarType? type)
        {
            if (type == Models.ScriptVarType.BOOL)
            {
                return STValue.FromBool(raw is "TRUE" or "true" or "1");
            }
            if (type == Models.ScriptVarType.STRING)
            {
                return STValue.FromString(raw);
            }
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            {
                if (type == Models.ScriptVarType.INT) return STValue.FromInt((long)num);
                return STValue.FromReal(num);
            }
            return STValue.FromReal(0);
        }

        private static STValue ZeroOf(Models.ScriptVarType? type) => type switch
        {
            Models.ScriptVarType.BOOL => STValue.FromBool(false),
            Models.ScriptVarType.INT => STValue.FromInt(0),
            Models.ScriptVarType.STRING => STValue.FromString(""),
            Models.ScriptVarType.TIME => STValue.FromTime(TimeSpan.Zero),
            _ => STValue.FromReal(0),
        };

        private void Set(string name, STValue value, AstNode node)
        {
            if (tempStore.ContainsKey(name)) { tempStore[name] = value; return; }
            if (staticStore.ContainsKey(name)) { staticStore[name] = value; return; }
            if (ctx.Globals.ContainsKey(name)) { ctx.Globals[name] = value; return; }
            // writing an Output aspect: cache it and record it as a script output
            var aspect = FindAspect(name);
            if (aspect is not null)
            {
                if (aspect.Category != Models.AspectCategory.Output)
                {
                    ctx.RuntimeIssues.Add(ScriptDiagnostic.Error(
                        $"'{name}' is a read-only {aspect.Category} variable and cannot be assigned.", node.Line, node.Column));
                    return;
                }
                aspectCache[name] = value;
                outputs[name] = value;
                return;
            }
            ctx.RuntimeIssues.Add(ScriptDiagnostic.Error($"'{name}' is not declared.", node.Line, node.Column));
        }

        private STValue Eval(Expression expr)
        {
            return expr switch
            {
                Literal l => ToValue(l),
                IdentifierExpr id => EvalIdentifier(id),
                UnaryExpr u => EvalUnary(u),
                BinaryExpr b => EvalBinary(b),
                CallExpr c => EvalCall(c),
                IndexExpr ix => EvalIndex(ix),
                _ => STValue.Void,
            };
        }

        private STValue ToValue(Literal l) => l.LiteralType switch
        {
            "BOOL" => STValue.FromBool((bool)l.Value!),
            "INT" => STValue.FromInt((long)l.Value!),
            "REAL" => STValue.FromReal((double)l.Value!),
            "STRING" => STValue.FromString((string)l.Value!),
            "TIME" => STValue.FromTime((TimeSpan)l.Value!),
            _ => STValue.Void,
        };

        private STValue EvalIdentifier(IdentifierExpr id)
        {
            if (TryGet(id.Name, out var v)) return v;
            ctx.RuntimeIssues.Add(ScriptDiagnostic.Error($"'{id.Name}' is not declared.", id.Line, id.Column));
            return STValue.Void;
        }

        private STValue EvalUnary(UnaryExpr u)
        {
            var operand = Eval(u.Operand);
            return u.Op switch
            {
                "NOT" => STValue.FromBool(!operand.AsBool()),
                "-" => operand.Type == STValueType.Int ? STValue.FromInt(-operand.AsInt()) : STValue.FromReal(-operand.AsReal()),
                _ => operand,
            };
        }

        private STValue EvalBinary(BinaryExpr b)
        {
            // short-circuit logical ops
            if (b.Op == "AND")
            {
                var l = Eval(b.Left);
                if (!l.AsBool()) return STValue.FromBool(false);
                return STValue.FromBool(Eval(b.Right).AsBool());
            }
            if (b.Op == "OR")
            {
                var l = Eval(b.Left);
                if (l.AsBool()) return STValue.FromBool(true);
                return STValue.FromBool(Eval(b.Right).AsBool());
            }

            var left = Eval(b.Left);
            var right = Eval(b.Right);

            switch (b.Op)
            {
                case "XOR": return STValue.FromBool(left.AsBool() != right.AsBool());
                case "+":
                    if (left.Type == STValueType.String || right.Type == STValueType.String)
                        return STValue.FromString(left.AsString() + right.AsString());
                    return Numeric(left, right, (x, y) => x + y, (x, y) => x + y);
                case "-": return Numeric(left, right, (x, y) => x - y, (x, y) => x - y);
                case "*": return Numeric(left, right, (x, y) => x * y, (x, y) => x * y);
                case "/":
                    if (right.AsReal() == 0)
                    {
                        ctx.RuntimeIssues.Add(ScriptDiagnostic.Error("Division by zero.", b.Line, b.Column));
                        return STValue.FromReal(0);
                    }
                    return STValue.FromReal(left.AsReal() / right.AsReal());
                case "MOD":
                    if (right.AsInt() == 0)
                    {
                        ctx.RuntimeIssues.Add(ScriptDiagnostic.Error("MOD by zero.", b.Line, b.Column));
                        return STValue.FromInt(0);
                    }
                    return STValue.FromInt(left.AsInt() % right.AsInt());
                case "=": return STValue.FromBool(EqualsFor(left, right));
                case "<>": return STValue.FromBool(!EqualsFor(left, right));
                case "<": return STValue.FromBool(Compare(left, right) < 0);
                case "<=": return STValue.FromBool(Compare(left, right) <= 0);
                case ">": return STValue.FromBool(Compare(left, right) > 0);
                case ">=": return STValue.FromBool(Compare(left, right) >= 0);
                default: return STValue.Void;
            }
        }

        private STValue Numeric(STValue l, STValue r, Func<long, long, long> intOp, Func<double, double, double> realOp)
        {
            if (l.Type == STValueType.Int && r.Type == STValueType.Int)
                return STValue.FromInt(intOp(l.AsInt(), r.AsInt()));
            return STValue.FromReal(realOp(l.AsReal(), r.AsReal()));
        }

        private bool EqualsFor(STValue l, STValue r)
        {
            if (l.Type == STValueType.String || r.Type == STValueType.String)
                return l.AsString() == r.AsString();
            if (l.IsNumeric && r.IsNumeric)
            {
                if (l.Type == STValueType.Int && r.Type == STValueType.Int) return l.AsInt() == r.AsInt();
                return l.AsReal() == r.AsReal();
            }
            if (l.Type == STValueType.Bool && r.Type == STValueType.Bool) return l.AsBool() == r.AsBool();
            if (l.Type == STValueType.Time && r.Type == STValueType.Time) return l.AsTime() == r.AsTime();
            return false;
        }

        private int Compare(STValue l, STValue r)
        {
            if (l.Type == STValueType.String || r.Type == STValueType.String)
                return string.CompareOrdinal(l.AsString(), r.AsString());
            if (l.Type == STValueType.Time && r.Type == STValueType.Time)
                return l.AsTime().CompareTo(r.AsTime());
            return l.AsReal().CompareTo(r.AsReal());
        }

        private STValue EvalIndex(IndexExpr ix)
        {
            // array support arrives in a later phase; for now report a clear runtime issue
            ctx.RuntimeIssues.Add(ScriptDiagnostic.Error("Array indexing is not supported yet.", ix.Line, ix.Column));
            return STValue.Void;
        }

        private STValue EvalCall(CallExpr c)
        {
            var name = c.FunctionName;
            // host interface
            switch (name)
            {
                case "Cycle": return STValue.FromInt(ctx.Cycle);
                case "IsActive": return STValue.FromBool(ctx.Function.isActive());
                case "GetInput": return HostGet("I", c);
                case "GetPrecondition": return HostGet("P", c);
                case "GetResource": return HostGet("R", c);
                case "GetControl": return HostGet("C", c);
                case "GetTime": return HostGet("T", c);
                case "SetOutput": return HostSetOutput(c);
                case "Print": case "Trace":
                    var msg = c.Arguments.Count > 0 ? Eval(c.Arguments[0]).AsString() : "";
                    ctx.Trace.Add($"[fn {ctx.Function.IDNr} cycle {ctx.Cycle}] {msg}");
                    return STValue.Void;
            }
            // numeric built-ins
            var args = c.Arguments.Select(Eval).ToList();
            double Arg(int i) => i < args.Count ? args[i].AsReal() : 0;
            return name.ToUpperInvariant() switch
            {
                "ABS" => STValue.FromReal(Math.Abs(Arg(0))),
                "SQRT" => STValue.FromReal(Math.Sqrt(Arg(0))),
                "SIN" => STValue.FromReal(Math.Sin(Arg(0))),
                "COS" => STValue.FromReal(Math.Cos(Arg(0))),
                "TAN" => STValue.FromReal(Math.Tan(Arg(0))),
                "LN" => STValue.FromReal(Math.Log(Arg(0))),
                "EXP" => STValue.FromReal(Math.Exp(Arg(0))),
                "MIN" => STValue.FromReal(args.Count == 0 ? 0 : args.Min(a => a.AsReal())),
                "MAX" => STValue.FromReal(args.Count == 0 ? 0 : args.Max(a => a.AsReal())),
                "LIMIT" => STValue.FromReal(Math.Min(Math.Max(Arg(1), Arg(0)), Arg(2))), // LIMIT(mn, x, mx)
                "ROUND" => STValue.FromInt((long)Math.Round(Arg(0))),
                "TRUNC" => STValue.FromInt((long)Math.Truncate(Arg(0))),
                _ => UnknownFunction(c),
            };
        }

        private STValue UnknownFunction(CallExpr c)
        {
            ctx.RuntimeIssues.Add(ScriptDiagnostic.Error($"Unknown function '{c.FunctionName}'.", c.Line, c.Column));
            return STValue.Void;
        }

        private STValue HostGet(string aspectLetter, CallExpr c)
        {
            var key = c.Arguments.Count > 0 ? Eval(c.Arguments[0]).AsString() : "";
            // FMI variables are named like "{Type}{outputFn}_{Key}" on the target function
            var vars = ctx.Function.getVariables();
            foreach (var kv in vars)
            {
                if (kv.Key.StartsWith(aspectLetter, StringComparison.OrdinalIgnoreCase)
                    && kv.Key.EndsWith("_" + key, StringComparison.OrdinalIgnoreCase))
                {
                    var raw = kv.Value is { Length: > 0 } ? kv.Value[0] : "";
                    if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var num))
                    {
                        return STValue.FromReal(num);
                    }
                    return STValue.FromString(raw);
                }
            }
            return STValue.FromReal(0);
        }

        private STValue HostSetOutput(CallExpr c)
        {
            var key = c.Arguments.Count > 0 ? Eval(c.Arguments[0]).AsString() : "";
            var value = c.Arguments.Count > 1 ? Eval(c.Arguments[1]) : STValue.Void;
            outputs[key] = value;
            return STValue.Void;
        }
    }
}
