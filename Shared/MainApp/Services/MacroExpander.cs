using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// Typed expression evaluator for macro-adjacent expressions. When an expression contains at
    /// least one macro call it is evaluated here instead of the string-based ExpressionEvaluator,
    /// so macro arguments can be checked strictly (no coercion at macro boundaries).
    /// </summary>
    public sealed class MacroExpander
    {
        public const int MaxDepth = 10;

        private static readonly Regex CalleePattern = new(@"([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled);

        //Legacy built-ins plus the statement/array forms handled before EvaluateExpression in the
        //interpreter. Keeping them here means they are never mistaken for unknown macros.
        private static readonly HashSet<string> BuiltIns = new(StringComparer.Ordinal)
        {
            "lookup", "xref", "rnorm", "rnormi", "rnd", "rndi", "min", "max", "api",
            "round", "sqrt", "sin", "cos", "tan", "abs", "exp", "log", "ceiling", "floor", "int",
            "execute", "array"
        };

        private readonly IMacroRegistry _registry;
        private readonly Random _random = new();
        private readonly ConcurrentDictionary<(string Name, string FunctionId), MacroAstNode> _bodyCache = new();

        public MacroExpander(IMacroRegistry registry)
        {
            _registry = registry;
            registry.Changed += () => _bodyCache.Clear();
        }

        public static bool IsBuiltIn(string name) => BuiltIns.Contains(name);

        /// <summary>
        /// Lightweight scan for call-callee identifiers, used by the registry's static body scan.
        /// String literals are blanked first so text inside quotes is ignored.
        /// </summary>
        public static List<string> FindCallCallees(string expression)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(expression))
            {
                return result;
            }
            var sb = new StringBuilder(expression.Length);
            var inString = false;
            foreach (var c in expression)
            {
                if (c == '"')
                {
                    inString = !inString;
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(inString ? ' ' : c);
                }
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in CalleePattern.Matches(sb.ToString()))
            {
                var name = m.Groups[1].Value;
                if (seen.Add(name))
                {
                    result.Add(name);
                }
            }
            return result;
        }

        /// <summary>
        /// Evaluates <paramref name="expression"/> in typed mode and returns the legacy string
        /// representation. All macro-level failures (arity, type, cycle, depth, rule-in-value,
        /// unknown callee) emit a diagnostic and fail-soft to "".
        /// </summary>
        public string Expand(string expression, Dictionary<string, string[]> variables, string callingFunctionId, FmiEvaluationContext ctx, List<FmiDiagnostic> diagnostics)
        {
            try
            {
                var root = new Parser(expression ?? "").Parse();
                var state = new EvalState(variables, callingFunctionId, ctx, diagnostics, 0, new List<string>(), new Dictionary<string, FmiValue>());
                return Evaluate(root, state).ToLegacyString();
            }
            catch (FormatException fe)
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, "macro", fe.Message));
                return "";
            }
        }

        private FmiValue Evaluate(MacroAstNode node, EvalState state)
        {
            switch (node)
            {
                case LiteralAst lit:
                    return lit.Value;
                case IdentifierAst id:
                    return ResolveIdentifier(id.Name, state);
                case UnaryAst unary:
                    return EvaluateUnary(unary, state);
                case BinaryAst binary:
                    return EvaluateBinary(binary, state);
                case ConditionalAst conditional:
                    return Evaluate(conditional.Condition, state).AsBool()
                        ? Evaluate(conditional.WhenTrue, state)
                        : Evaluate(conditional.WhenFalse, state);
                case CallAst call:
                    return EvaluateCall(call, state);
                default:
                    return FmiValue.Empty;
            }
        }

        private static FmiValue ResolveIdentifier(string name, EvalState state)
        {
            if (state.Parameters.TryGetValue(name, out var bound))
            {
                return bound;
            }
            if (state.Variables.TryGetValue(name, out var cells))
            {
                return FmiValue.FromLegacyString(cells is { Length: > 0 } ? cells[0] : "");
            }
            if (name.Length > 0 && char.IsUpper(name[0]))
            {
                return FmiValue.Number(0); //legacy: unknown uppercase variable reads as 0
            }
            return FmiValue.Str(name); //legacy: unknown lowercase identifier reads as itself
        }

        private FmiValue EvaluateUnary(UnaryAst unary, EvalState state)
        {
            var value = Evaluate(unary.Operand, state);
            return unary.Op switch
            {
                "-" => FmiValue.Number(-value.AsNumber()),
                "!" => FmiValue.Bool(!value.AsBool()),
                _ => value
            };
        }

        private FmiValue EvaluateBinary(BinaryAst binary, EvalState state)
        {
            var left = Evaluate(binary.Left, state);
            var right = Evaluate(binary.Right, state);
            switch (binary.Op)
            {
                case "+": return FmiValue.Number(left.AsNumber() + right.AsNumber());
                case "-": return FmiValue.Number(left.AsNumber() - right.AsNumber());
                case "*": return FmiValue.Number(left.AsNumber() * right.AsNumber());
                case "/": return FmiValue.Number(left.AsNumber() / right.AsNumber());
                case "^": return FmiValue.Number(Math.Pow(left.AsNumber(), right.AsNumber()));
                case "&&": return FmiValue.Bool(left.AsBool() && right.AsBool());
                case "||": return FmiValue.Bool(left.AsBool() || right.AsBool());
                case "==": return FmiValue.Bool(ValuesEqual(left, right));
                case "!=": return FmiValue.Bool(!ValuesEqual(left, right));
                case "<": return FmiValue.Bool(Compare(left, right) < 0);
                case "<=": return FmiValue.Bool(Compare(left, right) <= 0);
                case ">": return FmiValue.Bool(Compare(left, right) > 0);
                case ">=": return FmiValue.Bool(Compare(left, right) >= 0);
                default: return FmiValue.Empty;
            }
        }

        private static bool ValuesEqual(FmiValue left, FmiValue right)
        {
            if (left.Type == FmiValueType.String && right.Type == FmiValueType.String)
            {
                return string.Equals(left.AsString(), right.AsString(), StringComparison.Ordinal);
            }
            return left.AsNumber() == right.AsNumber();
        }

        private static int Compare(FmiValue left, FmiValue right)
        {
            if (left.Type == FmiValueType.String && right.Type == FmiValueType.String)
            {
                return string.CompareOrdinal(left.AsString(), right.AsString());
            }
            return left.AsNumber().CompareTo(right.AsNumber());
        }

        private FmiValue EvaluateCall(CallAst call, EvalState state)
        {
            var name = call.Name;
            if (_registry.TryGetMacro(name, state.CallingFunctionId, out var macro))
            {
                return ExpandMacro(macro, call.Args, state);
            }
            if (BuiltIns.Contains(name))
            {
                return EvaluateBuiltIn(name, call.Args, state);
            }
            state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{name}",
                $"Unknown macro '{name}'. It is not a built-in function and no macro with that name is visible here."));
            return FmiValue.Empty;
        }

        private FmiValue ExpandMacro(FmiMacroDefinition macro, List<MacroAstNode> args, EvalState state)
        {
            if (macro.Kind == FmiMacroKind.Rule)
            {
                state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Rule macro '{macro.Name}' cannot be used in a value expression."));
                return FmiValue.Empty;
            }
            if (_registry.IsPoisoned(macro))
            {
                state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Macro '{macro.Name}' is part of a circular reference and cannot be expanded."));
                return FmiValue.Empty;
            }
            if (state.Depth >= MaxDepth)
            {
                state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Macro expansion exceeded the maximum depth of {MaxDepth}."));
                return FmiValue.Empty;
            }
            if (state.Stack.Contains(macro.Name))
            {
                state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Circular macro reference detected while expanding '{macro.Name}'."));
                return FmiValue.Empty;
            }
            if (args.Count != macro.Parameters.Count)
            {
                state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Macro '{macro.Name}' expects {macro.Parameters.Count} argument(s) but received {args.Count}."));
                return FmiValue.Empty;
            }

            var bound = new Dictionary<string, FmiValue>();
            for (var i = 0; i < args.Count; i++)
            {
                var argValue = Evaluate(args[i], state);
                var parameter = macro.Parameters[i];
                if (argValue.Type != parameter.Type.Kind)
                {
                    state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                        $"Macro '{macro.Name}' parameter '{parameter.Name}' expects {parameter.Type} but received {TypeName(argValue.Type)}."));
                    return FmiValue.Empty;
                }
                bound[parameter.Name] = argValue;
            }

            MacroAstNode body;
            try
            {
                body = _bodyCache.GetOrAdd((macro.Name, macro.FunctionId), _ => new Parser(macro.Body).Parse());
            }
            catch (FormatException fe)
            {
                state.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Macro '{macro.Name}' body could not be parsed: {fe.Message}"));
                return FmiValue.Empty;
            }

            var child = new EvalState(state.Variables, state.CallingFunctionId, state.Ctx, state.Diagnostics,
                state.Depth + 1, new List<string>(state.Stack) { macro.Name }, bound);
            return Evaluate(body, child);
        }

        private FmiValue EvaluateBuiltIn(string name, List<MacroAstNode> args, EvalState state)
        {
            var values = args.Select(a => Evaluate(a, state)).ToList();
            var first = values.Count > 0 ? values[0].AsNumber() : 0;
            switch (name)
            {
                case "min": return FmiValue.Number(values.Count == 0 ? 0 : values.Min(v => v.AsNumber()));
                case "max": return FmiValue.Number(values.Count == 0 ? 0 : values.Max(v => v.AsNumber()));
                case "round": return FmiValue.Number(Math.Round(first));
                case "sqrt": return FmiValue.Number(Math.Sqrt(first));
                case "sin": return FmiValue.Number(Math.Sin(first));
                case "cos": return FmiValue.Number(Math.Cos(first));
                case "tan": return FmiValue.Number(Math.Tan(first));
                case "abs": return FmiValue.Number(Math.Abs(first));
                case "exp": return FmiValue.Number(Math.Exp(first));
                case "log": return FmiValue.Number(Math.Log(first));
                case "ceiling": return FmiValue.Number(Math.Ceiling(first));
                case "floor": return FmiValue.Number(Math.Floor(first));
                case "int": return FmiValue.Number(Math.Truncate(first));
                case "rnd": return FmiValue.Number(values.Count == 0 ? _random.NextDouble() : _random.NextDouble() * first);
                case "rndi": return FmiValue.Number(values.Count == 0 ? 0 : Math.Floor(_random.NextDouble() * first));
                case "rnorm": return FmiValue.Number(RandomNormal(values.Count > 0 ? values[0].AsNumber() : 0, values.Count > 1 ? values[1].AsNumber() : 1));
                case "rnormi": return FmiValue.Number(Math.Round(RandomNormal(values.Count > 0 ? values[0].AsNumber() : 0, values.Count > 1 ? values[1].AsNumber() : 1)));
                case "lookup": return EvaluateLookup(values, state);
                case "xref": return EvaluateXref(values, state);
                case "api": return FmiValue.Str("NaN");
                case "execute":
                case "array":
                    return FmiValue.Str(name);
                default:
                    return FmiValue.Empty;
            }
        }

        private double RandomNormal(double mean, double stddev)
        {
            var u1 = 1.0 - _random.NextDouble();
            var u2 = _random.NextDouble();
            return mean + stddev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        private static FmiValue EvaluateLookup(List<FmiValue> values, EvalState state)
        {
            if (state.Ctx.DataRows.Count == 0 || values.Count < 2)
            {
                return FmiValue.Str("NaN");
            }
            var row = (int)values[0].AsNumber();
            var col = (int)values[1].AsNumber();
            if (row < 0 || col < 0 || row >= state.Ctx.DataRows.Count || col >= state.Ctx.DataRows[row].Length)
            {
                return FmiValue.Str("NaN");
            }
            return FmiValue.Str(state.Ctx.DataRows[row][col]);
        }

        private static FmiValue EvaluateXref(List<FmiValue> values, EvalState state)
        {
            if (state.Ctx.DataKeys.Count == 0 || values.Count < 2)
            {
                return FmiValue.Str("NaN");
            }
            var key = $"{values[0].ToLegacyString()},{values[1].ToLegacyString()}";
            return state.Ctx.DataKeys.TryGetValue(key, out var value) ? FmiValue.Str(value) : FmiValue.Str("NaN");
        }

        private static string TypeName(FmiValueType type)
        {
            return type switch
            {
                FmiValueType.Empty => "empty",
                FmiValueType.Number => "number",
                FmiValueType.Boolean => "bool",
                FmiValueType.String => "string",
                FmiValueType.Array => "array",
                FmiValueType.List => "list",
                FmiValueType.Dictionary => "dictionary",
                _ => "unknown"
            };
        }

        private sealed class EvalState
        {
            public Dictionary<string, string[]> Variables { get; }
            public string CallingFunctionId { get; }
            public FmiEvaluationContext Ctx { get; }
            public List<FmiDiagnostic> Diagnostics { get; }
            public int Depth { get; }
            public List<string> Stack { get; }
            public Dictionary<string, FmiValue> Parameters { get; }

            public EvalState(Dictionary<string, string[]> variables, string callingFunctionId, FmiEvaluationContext ctx,
                List<FmiDiagnostic> diagnostics, int depth, List<string> stack, Dictionary<string, FmiValue> parameters)
            {
                Variables = variables;
                CallingFunctionId = callingFunctionId;
                Ctx = ctx;
                Diagnostics = diagnostics;
                Depth = depth;
                Stack = stack;
                Parameters = parameters;
            }
        }

        private abstract class MacroAstNode { }

        private sealed class LiteralAst : MacroAstNode
        {
            public FmiValue Value { get; }
            public LiteralAst(FmiValue value) => Value = value;
        }

        private sealed class IdentifierAst : MacroAstNode
        {
            public string Name { get; }
            public IdentifierAst(string name) => Name = name;
        }

        private sealed class UnaryAst : MacroAstNode
        {
            public string Op { get; }
            public MacroAstNode Operand { get; }
            public UnaryAst(string op, MacroAstNode operand) { Op = op; Operand = operand; }
        }

        private sealed class BinaryAst : MacroAstNode
        {
            public string Op { get; }
            public MacroAstNode Left { get; }
            public MacroAstNode Right { get; }
            public BinaryAst(string op, MacroAstNode left, MacroAstNode right) { Op = op; Left = left; Right = right; }
        }

        private sealed class ConditionalAst : MacroAstNode
        {
            public MacroAstNode Condition { get; }
            public MacroAstNode WhenTrue { get; }
            public MacroAstNode WhenFalse { get; }
            public ConditionalAst(MacroAstNode condition, MacroAstNode whenTrue, MacroAstNode whenFalse)
            {
                Condition = condition;
                WhenTrue = whenTrue;
                WhenFalse = whenFalse;
            }
        }

        private sealed class CallAst : MacroAstNode
        {
            public string Name { get; }
            public List<MacroAstNode> Args { get; }
            public CallAst(string name, List<MacroAstNode> args) { Name = name; Args = args; }
        }

        private sealed class Parser
        {
            private readonly string _src;
            private int _pos = -1;
            private char _ch = '\0';

            public Parser(string src) => _src = src;

            public MacroAstNode Parse()
            {
                Next();
                var node = ParseConditional();
                SkipSpaces();
                if (_pos < _src.Length)
                {
                    throw new FormatException($"Unexpected character '{_ch}' in macro expression.");
                }
                return node;
            }

            private void Next()
            {
                if (++_pos < _src.Length)
                {
                    _ch = _src[_pos];
                }
                else
                {
                    _ch = '\0';
                }
            }

            private char PeekNext() => _pos + 1 < _src.Length ? _src[_pos + 1] : '\0';

            private void SkipSpaces()
            {
                while (_ch == ' ')
                {
                    Next();
                }
            }

            private bool Eat(char c)
            {
                SkipSpaces();
                if (_ch == c)
                {
                    Next();
                    return true;
                }
                return false;
            }

            private void Expect(char c)
            {
                if (!Eat(c))
                {
                    throw new FormatException($"Expected '{c}' in macro expression.");
                }
            }

            private MacroAstNode ParseConditional()
            {
                var condition = ParseOr();
                if (Eat('?'))
                {
                    var whenTrue = ParseConditional();
                    Expect(':');
                    var whenFalse = ParseConditional();
                    return new ConditionalAst(condition, whenTrue, whenFalse);
                }
                return condition;
            }

            private MacroAstNode ParseOr()
            {
                var left = ParseAnd();
                while (Eat('|'))
                {
                    Eat('|');
                    left = new BinaryAst("||", left, ParseAnd());
                }
                return left;
            }

            private MacroAstNode ParseAnd()
            {
                var left = ParseEquality();
                while (Eat('&'))
                {
                    Eat('&');
                    left = new BinaryAst("&&", left, ParseEquality());
                }
                return left;
            }

            private MacroAstNode ParseEquality()
            {
                var left = ParseRelational();
                while (true)
                {
                    if (Eat('='))
                    {
                        Eat('=');
                        left = new BinaryAst("==", left, ParseRelational());
                    }
                    else if (_ch == '!' && PeekNext() == '=')
                    {
                        Next();
                        Next();
                        left = new BinaryAst("!=", left, ParseRelational());
                    }
                    else if (_ch == '<' && PeekNext() == '>')
                    {
                        Next();
                        Next();
                        left = new BinaryAst("!=", left, ParseRelational());
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            private MacroAstNode ParseRelational()
            {
                var left = ParseAdditive();
                while (true)
                {
                    if (Eat('<'))
                    {
                        left = Eat('=') ? new BinaryAst("<=", left, ParseAdditive()) : new BinaryAst("<", left, ParseAdditive());
                    }
                    else if (Eat('>'))
                    {
                        left = Eat('=') ? new BinaryAst(">=", left, ParseAdditive()) : new BinaryAst(">", left, ParseAdditive());
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            private MacroAstNode ParseAdditive()
            {
                var left = ParseMultiplicative();
                while (true)
                {
                    if (Eat('+'))
                    {
                        left = new BinaryAst("+", left, ParseMultiplicative());
                    }
                    else if (Eat('-'))
                    {
                        left = new BinaryAst("-", left, ParseMultiplicative());
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            private MacroAstNode ParseMultiplicative()
            {
                var left = ParsePower();
                while (true)
                {
                    if (Eat('*'))
                    {
                        left = new BinaryAst("*", left, ParsePower());
                    }
                    else if (Eat('/'))
                    {
                        left = new BinaryAst("/", left, ParsePower());
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            private MacroAstNode ParsePower()
            {
                var left = ParseUnary();
                if (Eat('^'))
                {
                    return new BinaryAst("^", left, ParsePower());
                }
                return left;
            }

            private MacroAstNode ParseUnary()
            {
                if (Eat('+'))
                {
                    return ParseUnary();
                }
                if (Eat('-'))
                {
                    return new UnaryAst("-", ParseUnary());
                }
                if (Eat('!'))
                {
                    return new UnaryAst("!", ParseUnary());
                }
                return ParsePrimary();
            }

            private MacroAstNode ParsePrimary()
            {
                SkipSpaces();
                if (_ch == '(')
                {
                    Next();
                    var node = ParseConditional();
                    Expect(')');
                    return node;
                }
                if (_ch == '"')
                {
                    Next();
                    var sb = new StringBuilder();
                    while (_ch != '"' && _ch != '\0')
                    {
                        sb.Append(_ch);
                        Next();
                    }
                    if (_ch == '"')
                    {
                        Next();
                    }
                    return new LiteralAst(FmiValue.Str(sb.ToString()));
                }
                if (_ch == 'π')
                {
                    Next();
                    return new LiteralAst(FmiValue.Number(Math.PI));
                }
                if (char.IsDigit(_ch) || _ch == '.')
                {
                    return ParseNumber();
                }
                if (char.IsLetter(_ch) || _ch == '_')
                {
                    return ParseIdentifierOrCall();
                }
                throw new FormatException($"Unexpected character '{_ch}' in macro expression.");
            }

            private LiteralAst ParseNumber()
            {
                var sb = new StringBuilder();
                while (char.IsDigit(_ch) || _ch == '.')
                {
                    sb.Append(_ch);
                    Next();
                }
                var text = sb.ToString();
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    throw new FormatException($"Invalid number '{text}' in macro expression.");
                }
                return new LiteralAst(FmiValue.Number(value));
            }

            private MacroAstNode ParseIdentifierOrCall()
            {
                var sb = new StringBuilder();
                while (char.IsLetterOrDigit(_ch) || _ch == '_')
                {
                    sb.Append(_ch);
                    Next();
                }
                var name = sb.ToString();
                SkipSpaces();
                if (_ch != '(')
                {
                    return new IdentifierAst(name);
                }
                Next();
                var args = new List<MacroAstNode>();
                SkipSpaces();
                if (Eat(')'))
                {
                    return new CallAst(name, args);
                }
                while (true)
                {
                    args.Add(ParseConditional());
                    SkipSpaces();
                    if (Eat(','))
                    {
                        continue;
                    }
                    if (_ch == ')')
                    {
                        Next();
                        break;
                    }
                    throw new FormatException($"Expected ',' or ')' in call to '{name}'.");
                }
                return new CallAst(name, args);
            }
        }
    }
}
