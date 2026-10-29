using System.Collections.Generic;
using System.Linq;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services;
using FRAMLab.Shared.MainApp.Services.Logging;
using Microsoft.Extensions.Logging;

namespace FRAMLab.Shared.MainApp.Services.Scripting
{
    /// <summary>
    /// Facade that ties the ST compiler and interpreter to the FRAM model. Builds the
    /// symbol table (declared variables + aspect variables + host interface) for a
    /// function, compiles its script, and executes it per FMI cycle with the correct
    /// Temp/Static/Global scoping.
    /// </summary>
    public sealed class ScriptingService
    {
        private readonly ILogger<ScriptingService> logger;

        public ScriptingService(ILogger<ScriptingService> logger) => this.logger = logger;

        /// <summary>Per-function Static scope stores, kept for a whole FMI run (key = function IDNr).</summary>
        private readonly Dictionary<string, Dictionary<string, STValue>> staticStores = new();
        /// <summary>Global scope store, shared across all functions, kept for a whole FMI run.</summary>
        private readonly Dictionary<string, STValue> globalStore = new(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Clear all Static and Global stores (called on FMI reset).</summary>
        public void Reset()
        {
            staticStores.Clear();
            globalStore.Clear();
        }

        public Dictionary<string, STValue> GlobalStore => globalStore;

        private Dictionary<string, STValue> StaticStoreFor(string functionIdNr)
        {
            if (!staticStores.TryGetValue(functionIdNr, out var store))
            {
                store = new Dictionary<string, STValue>(System.StringComparer.OrdinalIgnoreCase);
                staticStores[functionIdNr] = store;
            }
            return store;
        }

        /// <summary>Build the symbol table a function's script is compiled against.</summary>
        public SymbolTable BuildSymbols(Function fn)
        {
            var symbols = new SymbolTable();

            // declared local/static variables (writable)
            foreach (var v in fn.Script.Variables)
            {
                if (string.IsNullOrWhiteSpace(v.Name)) continue;
                symbols.Add(new Symbol(v.Name, v.Type.ToString(), isWritable: true, v.Scope.ToString(),
                    isArray: v.IsArray, lowerBound: v.LowerBound, upperBound: v.UpperBound,
                    hasInitialValue: v.HasInitialValue, rank: v.Dimensions));
            }
            // global variables (writable), from the shared global declarations
            foreach (var gv in GlobalVariableDeclarations)
            {
                if (string.IsNullOrWhiteSpace(gv.Name)) continue;
                symbols.Add(new Symbol(gv.Name, gv.Type.ToString(), isWritable: true, "Global",
                    isArray: gv.IsArray, lowerBound: gv.LowerBound, upperBound: gv.UpperBound,
                    hasInitialValue: gv.HasInitialValue, rank: gv.Dimensions));
            }
            // aspect variables: inputs are read-only, outputs are writable via assignment.
            // Registered under their sanitized identifier (labels may contain spaces).
            foreach (var av in fn.Script.AspectVariables)
            {
                if (string.IsNullOrWhiteSpace(av.Label)) continue;
                var writable = av.Category == AspectCategory.Output;
                symbols.Add(new Symbol(av.Identifier, av.Type?.ToString() ?? "ANY", isWritable: writable, av.Category.ToString()));
            }
            // built-in read-only cycle variable
            symbols.Add(new Symbol("Cycle", "INT", isWritable: false, "Builtin"));
            return symbols;
        }

        /// <summary>Live data for API('source', 'path'); set from Program.cs.</summary>
        public FmiDataService? Data { get; set; }

        private FmiApiReader? ApiReaderFor(Function fn)
        {
            if (Data is null) return null;
            var data = Data;
            return (string source, string path, out string value, out string error) => data.TryRead(fn.IDNr, source, path, out value, out error);
        }

        /// <summary>Global variable declarations (set from the editor's Globals panel).</summary>
        public List<ScriptVariable> GlobalVariableDeclarations { get; } = new();

        /// <summary>Compile a function's script (does not execute).</summary>
        public CompiledScript Compile(Function fn)
        {
            var symbols = BuildSymbols(fn);
            return ScriptCompiler.Compile(fn.Script.Source, symbols, logger);
        }

        /// <summary>
        /// Execute a function's script for one FMI cycle. Returns the execution context
        /// (outputs, runtime issues, trace). Skips cleanly when disabled or on compile errors.
        /// </summary>
        public ScriptExecutionContext? ExecuteCycle(Function fn, int cycle)
        {
            if (!fn.Script.Enabled || string.IsNullOrWhiteSpace(fn.Script.Source))
            {
                return null;
            }

            var compiled = Compile(fn);
            if (compiled.HasErrors || compiled.Program is null)
            {
                // don't run broken code, but say why (the FMI log shows these like runtime errors)
                var failed = new ScriptExecutionContext(fn, cycle, globalStore);
                failed.RuntimeIssues.AddRange(compiled.Diagnostics.Where(d => d.Severity == "Error")
                    .Select(d => ScriptDiagnostic.Error($"Script not run, it does not compile: {d.Message}", d.StartLine, d.StartColumn)));
                return failed;
            }

            // ensure globals exist in the store: both the shared declarations from the
            // editor's Globals panel and any Global-scoped variable declared in this
            // function's own variable table (the IDE offers Global as a per-variable scope).
            foreach (var gv in GlobalVariableDeclarations)
            {
                if (!string.IsNullOrWhiteSpace(gv.Name) && !globalStore.ContainsKey(gv.Name))
                {
                    globalStore[gv.Name] = InitialOf(gv);
                }
            }
            foreach (var v in fn.Script.Variables)
            {
                if (v.Scope == ScriptScope.Global && !string.IsNullOrWhiteSpace(v.Name) && !globalStore.ContainsKey(v.Name))
                {
                    globalStore[v.Name] = InitialOf(v);
                }
            }

            var staticStore = StaticStoreFor(fn.IDNr);
            // lazily initialise Static variables on first touch
            foreach (var v in fn.Script.Variables)
            {
                if (v.Scope == ScriptScope.Static && !string.IsNullOrWhiteSpace(v.Name) && !staticStore.ContainsKey(v.Name))
                {
                    staticStore[v.Name] = InitialOf(v);
                }
            }

            var ctx = new ScriptExecutionContext(fn, cycle, globalStore) { Api = ApiReaderFor(fn) };
            ctx.DeclaredVariables = fn.Script.Variables
                .Concat(GlobalVariableDeclarations)
                .Where(v => !string.IsNullOrWhiteSpace(v.Name))
                .ToList();
            var interpreter = new STInterpreter(ctx, staticStore, logger);
            interpreter.Run(compiled.Program, fn.Script);

            // write Output-category variables and SetOutput calls back to the function
            WriteBackOutputs(fn, interpreter);
            return ctx;
        }

        /// <summary>
        /// Run one cycle of a function's script in isolation (the editor's "Run one cycle" panel).
        /// <paramref name="inputs"/> sets aspect values by identifier; <paramref name="state"/> sets
        /// Static/Global variables by name (empty = the declared initial value). Nothing is written
        /// back to the model and the run's Static/Global stores are not touched; pass a throwaway
        /// copy of the function, because aspect values are placed in its variables.
        /// </summary>
        public ScriptTestRunResult TestRun(Function fn, int cycle,
            IReadOnlyDictionary<string, string> inputs, IReadOnlyDictionary<string, string> state)
        {
            var compiled = Compile(fn);
            if (compiled.HasErrors || compiled.Program is null)
            {
                return new ScriptTestRunResult(compiled.Diagnostics.ToList(), new(), new(), new(), new());
            }

            STValue StartValue(ScriptVariable v) =>
                state.TryGetValue(v.Name, out var raw) && !string.IsNullOrWhiteSpace(raw)
                    ? InitialOf(new ScriptVariable { Name = v.Name, Type = v.Type, Scope = v.Scope, IsArray = v.IsArray, Dimensions = v.Dimensions, InitialValue = raw })
                    : InitialOf(v);

            var statics = new Dictionary<string, STValue>(System.StringComparer.OrdinalIgnoreCase);
            var globals = new Dictionary<string, STValue>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var v in fn.Script.Variables.Where(v => !string.IsNullOrWhiteSpace(v.Name)))
            {
                if (v.Scope == ScriptScope.Static) statics[v.Name] = StartValue(v);
                if (v.Scope == ScriptScope.Global) globals[v.Name] = StartValue(v);
            }
            foreach (var gv in GlobalVariableDeclarations.Where(v => !string.IsNullOrWhiteSpace(v.Name)))
            {
                globals[gv.Name] = StartValue(gv);
            }
            // aspect inputs are read from the function's variables under their identifier
            foreach (var (identifier, value) in inputs)
            {
                fn.newVariable(identifier, new[] { value, "test input" });
            }

            var ctx = new ScriptExecutionContext(fn, cycle, globals)
            {
                Api = ApiReaderFor(fn),
                DeclaredVariables = fn.Script.Variables.Concat(GlobalVariableDeclarations)
                    .Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList()
            };
            var interpreter = new STInterpreter(ctx, statics, logger);
            interpreter.Run(compiled.Program, fn.Script);

            static Dictionary<string, string> Show(IEnumerable<KeyValuePair<string, STValue>> values) =>
                values.ToDictionary(kv => kv.Key, kv => kv.Value.Type == STValueType.Uninitialized ? "(not set)" : kv.Value.AsString());
            return new ScriptTestRunResult(
                compiled.Diagnostics.Concat(ctx.RuntimeIssues).ToList(),
                ctx.Trace.ToList(),
                Show(interpreter.Outputs),
                Show(statics),
                Show(globals));
        }

        private void WriteBackOutputs(Function fn, STInterpreter interpreter)
        {
            foreach (var kv in interpreter.Outputs)
            {
                // map a sanitized aspect identifier back to the original FRAM label when possible
                var aspect = fn.Script.AspectVariables
                    .FirstOrDefault(a => a.Category == AspectCategory.Output
                                         && (string.Equals(a.Identifier, kv.Key, System.StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(a.Label, kv.Key, System.StringComparison.OrdinalIgnoreCase)));
                var label = aspect?.Label ?? kv.Key;
                // Write under the aspect's sanitized identifier (e.g. "Hazard_signal"). This
                // is the name the rest of the app uses: the coupling pipeline propagates
                // "{letter}{outputFn}_{key}" and calcVariables reads the producer's variable
                // by key, so a script output only reaches coupled functions when it is stored
                // under that identifier rather than a "Script_" alias.
                var key = aspect?.Identifier ?? kv.Key;
                var value = kv.Value.AsString();
                fn.newVariable(key, new[] { value, $"Script output ({label})" });
                // keep a human-readable alias so the FMI variable table still shows the output
                fn.newVariable($"Script_{label}", new[] { value, "script output" });
                // feed the value back into the function's metadata so coupling activation
                // (FMIControl.bFMI_cycle_Couplings) copies the *scripted* value downstream.
                // Only the current value [1] is updated; the initial value [4] must survive
                // so FMI Reset (and Save) restore the model's original value.
                var meta = fn.metadata?.FirstOrDefault(m =>
                    string.Equals(m[0], key, System.StringComparison.OrdinalIgnoreCase));
                if (meta is not null)
                {
                    meta[1] = value;
                }
            }
        }

        private STValue InitialOf(ScriptVariable v)
        {
            if (v.IsArray && !v.HasInitialValue)
            {
                return STValue.FromArray(new STArray(v.Type, rank: v.Dimensions)); // arrays start empty and grow on write
            }
            // No initial value: a scalar starts uninitialized and the script must
            // assign it before reading it (checked by the analyzer and interpreter).
            if (!v.HasInitialValue)
            {
                return STValue.Uninitialized;
            }
            try
            {
                if (v.IsArray)
                {
                    return STValue.FromArray(ScriptArrayFactory.Create(v));
                }
                return v.Type switch
                {
                    ScriptVarType.BOOL => STValue.FromBool(v.InitialValue is "TRUE" or "true" or "1"),
                    ScriptVarType.INT => STValue.FromInt(long.Parse(v.InitialValue, System.Globalization.CultureInfo.InvariantCulture)),
                    ScriptVarType.REAL => STValue.FromReal(double.Parse(v.InitialValue, System.Globalization.CultureInfo.InvariantCulture)),
                    ScriptVarType.STRING => STValue.FromString(v.InitialValue),
                    ScriptVarType.TIME => STValue.FromTime(ScriptArrayFactory.ParseTime(v.InitialValue)),
                    _ => STValue.FromReal(0),
                };
            }
            catch (System.Exception ex)
            {
                logger.LogWarning(LogEvents.ScriptInitialValue, ex,
                    "Invalid initial value '{InitialValue}' for {Scope} variable '{Name}' (type {Type}); treating it as uninitialized.",
                    v.InitialValue, v.Scope, v.Name, v.Type);
                return STValue.Uninitialized;
            }
        }
    }

    /// <summary>Outcome of <see cref="ScriptingService.TestRun"/>.</summary>
    public sealed record ScriptTestRunResult(
        List<ScriptDiagnostic> Issues,
        List<string> Trace,
        Dictionary<string, string> Outputs,
        Dictionary<string, string> Statics,
        Dictionary<string, string> Globals)
    {
        public bool HasErrors => Issues.Any(i => i.Severity == "Error");
    }
}
