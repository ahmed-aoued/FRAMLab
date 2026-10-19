using System.Collections.Generic;
using System.Linq;
using FMV_OG.Shared.MainApp.Models;
using FMV_OG.Shared.MainApp.Services.Logging;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services.Scripting
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
                symbols.Add(new Symbol(v.Name, v.Type.ToString(), isWritable: true, v.Scope.ToString()));
            }
            // global variables (writable), from the shared global declarations
            foreach (var gv in GlobalVariableDeclarations)
            {
                if (string.IsNullOrWhiteSpace(gv.Name)) continue;
                symbols.Add(new Symbol(gv.Name, gv.Type.ToString(), isWritable: true, "Global"));
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
                return null; // editor surfaces the compile diagnostics; don't run broken code
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

            var ctx = new ScriptExecutionContext(fn, cycle, globalStore);
            var interpreter = new STInterpreter(ctx, staticStore, logger);
            interpreter.Run(compiled.Program, fn.Script);

            // write Output-category variables and SetOutput calls back to the function
            WriteBackOutputs(fn, interpreter);
            return ctx;
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
                var meta = fn.metadata?.FirstOrDefault(m =>
                    string.Equals(m[0], key, System.StringComparison.OrdinalIgnoreCase));
                if (meta is not null)
                {
                    meta[1] = value;
                    meta[4] = value;
                }
            }
        }

        private STValue InitialOf(ScriptVariable v)
        {
            try
            {
                return v.Type switch
                {
                    ScriptVarType.BOOL => STValue.FromBool(v.InitialValue is "TRUE" or "true" or "1"),
                    ScriptVarType.INT => STValue.FromInt(long.Parse(v.InitialValue, System.Globalization.CultureInfo.InvariantCulture)),
                    ScriptVarType.REAL => STValue.FromReal(double.Parse(v.InitialValue, System.Globalization.CultureInfo.InvariantCulture)),
                    ScriptVarType.STRING => STValue.FromString(v.InitialValue),
                    ScriptVarType.TIME => STValue.FromTime(System.TimeSpan.Zero),
                    _ => STValue.FromReal(0),
                };
            }
            catch (System.Exception ex)
            {
                logger.LogWarning(LogEvents.ScriptInitialValue, ex,
                    "Invalid initial value '{InitialValue}' for {Scope} variable '{Name}' (type {Type}); defaulting to 0.",
                    v.InitialValue, v.Scope, v.Name, v.Type);
                return STValue.FromReal(0);
            }
        }
    }
}
