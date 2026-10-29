using System.Globalization;
using FRAMLab.Shared.MainApp.Services;
using FRAMLab.Shared.MainApp.Services.Logging;
using Microsoft.Extensions.Logging;

namespace FRAMLab.Shared
{
    /// <summary>
    /// Connects <see cref="FmiRunEngine"/> to the live application state: the model lists in
    /// <see cref="StateContainer"/>, the FMI interpreter (equations + macros), the ST scripting
    /// engine, the display tracers and the run log.
    /// </summary>
    public sealed class StateContainerFmiRunHost : IFmiRunHost
    {
        private readonly StateContainer state;
        private readonly ILogger logger;

        public StateContainerFmiRunHost(StateContainer state, ILogger logger)
        {
            this.state = state;
            this.logger = logger;
        }

        public List<Function> Functions => state.functionList;
        public List<Coupling> Couplings => state.couplingList;
        public List<Aspect> Aspects => state.aspectsList;
        public List<Aspect> Outputs => state.outputsList;
        public int Cycle
        {
            get => state.cycleFMI;
            set => state.cycleFMI = value;
        }

        public void BeginCycle() => state.fmiBeginCycle();
        public void EndCycle() => state.fmiEndCycle();

        public void StartLog()
        {
            state.SyncScriptAspects(); // a new run: pick up aspects added or renamed since the model was loaded
            state.clearFmiLog();
            state.appendFmiLog($"FRAM Model Interpreter - FMI Basic  -----  (c) Erik Hollnagel, 2020\nFMI session log: {DateTime.Now}\n\n");
            state.flushFmiDiagnostics(includeLoad: true); // definition + cycle-0 diagnostics
        }

        public void Log(string text, Function? fn = null, FmiLogLevel level = FmiLogLevel.Info)
            => state.appendFmiLog(text, level, level == FmiLogLevel.Info ? "Run" : "Model", fn);

        public void ShowLog() => state.showFMIPopup = true;

        public void SetFunctionType(Function fn, string functionType)
        {
            state.updateUndo();
            fn.FunctionType = functionType;
            state.setFunctionType(fn.IDNr, functionType, fn);
        }

        public async Task AfterActivateAsync(Function fn)
        {
            if (state.fmiData is not null) await state.fmiData.AfterActivateAsync(fn);
        }

        public async Task<IReadOnlyList<FmiUnknownVariable>> EvaluateAsync(Function fn)
        {
            if (state.fmiData is not null) await state.fmiData.BeforeEvaluateAsync(fn); // live data read on activation
            var showVariability = false;
            var result = state.fmiEvaluate(fn); //CalcVariables + metadata equations (and macros)
            state.flushFmiDiagnostics();
            ExecuteFunctionScript(fn); //the per-cycle ST script runs after the equations, so it can override them
            foreach (var (key, value) in result.EvaluatedKeys)
            {
                for (var i = 1; i <= 6; i++)
                {
                    if (state.displaySetup.ContainsKey($"tracer_{i}") && key == state.displaySetup[$"tracer_{i}"])
                    {
                        state.updateKvalue(fn, $"K{i}", value);
                        showVariability = true;
                    }
                }
            }
            if (showVariability && (fn.FunctionType != "2" || fn.fmiType == "Exit")) //ignore Background but include Exit functions
            {
                DrawTracerColours(fn);
            }
            state.addMetadataResults(fn); //a new row in the Metadata table for this function and cycle
            if (result.UnknownVariables.Count > 0)
            {
                fn.fmiHighlight = "hold-fill";
            }
            return result.UnknownVariables;
        }

        /// <summary>Run the function's ST script for this cycle, logging runtime issues and Print output.</summary>
        private void ExecuteFunctionScript(Function fn)
        {
            if (state.Scripting is null || fn.Script is null) return;
            if (!fn.Script.Enabled || string.IsNullOrWhiteSpace(fn.Script.Source)) return;
            try
            {
                // keep the scripting service's global declarations in sync with the model's globals
                state.Scripting.GlobalVariableDeclarations.Clear();
                state.Scripting.GlobalVariableDeclarations.AddRange(state.globalScriptVariables);
                var ctx = state.Scripting.ExecuteCycle(fn, state.cycleFMI);
                if (ctx is null) return;
                foreach (var issue in ctx.RuntimeIssues)
                {
                    var level = issue.Severity == "Error" ? FmiLogLevel.Error : issue.Severity == "Warning" ? FmiLogLevel.Warning : FmiLogLevel.Info;
                    state.appendFmiLog($"Script [{fn.IDName}] {issue.StartLine}:{issue.StartColumn} {issue.Message}\n", level, "Script", fn);
                    if (issue.Severity == "Error")
                    {
                        fn.fmiHighlight = "hold-fill";
                    }
                }
                foreach (var trace in ctx.Trace)
                {
                    state.appendFmiLog(trace + "\n", FmiLogLevel.Info, "Script", fn);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(LogEvents.FmiScript, ex,
                    "Script runtime error in function {FunctionName} ({FunctionId}) at cycle {Cycle}.",
                    fn.IDName, fn.IDNr, state.cycleFMI);
                state.appendFmiLog($"Script [{fn.IDName}] runtime error: {ex.Message}\n", FmiLogLevel.Error, "Script", fn);
                fn.fmiHighlight = "hold-fill";
            }
        }

        /// <summary>Colour the function by its tracer values (5 thresholds, 3 colours per tracer).</summary>
        private void DrawTracerColours(Function iBG)
        {
            iBG.toolTip = "";
            iBG.Cvalues = new Dictionary<string, string>();
            for (var i = 1; i <= 6; i++)
            {
                if (state.displaySetup[$"tracer_{i}"].Length > 0 && iBG.Kvalues.ContainsKey($"K{i}"))
                {
                    iBG.toolTip += $"{state.displaySetup[$"tracer_{i}"]} = {iBG.Kvalues[$"K{i}"]}\n";
                    double d_t0 = state.displayValue[$"colour_{i}0"];
                    double d_t1 = state.displayValue[$"colour_{i}1"];
                    double d_t2 = state.displayValue[$"colour_{i}2"];
                    double d_t3 = state.displayValue[$"colour_{i}3"];
                    double d_t4 = state.displayValue[$"colour_{i}4"];
                    var k = iBG.Kvalues[$"K{i}"];
                    if (d_t1 is not double.NaN && k <= d_t1)
                    {
                        iBG.Cvalues[$"{i}C_stroke"] = state.displaySetup[$"c_{i}0_value"];
                        iBG.Cvalues[$"{i}C_opacity"] = (0.75 - (k - d_t0) * 0.25 / (d_t1 - d_t0)).ToString("0.##", CultureInfo.InvariantCulture);
                        iBG.Cvalues[$"{i}Y_stroke"] = state.displaySetup[$"c_{i}2_value"];
                        iBG.Cvalues[$"{i}Y_opacity"] = ((k - d_t0) * 0.5 / (d_t1 - d_t0)).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                    else if (d_t2 is not double.NaN && k <= d_t2)
                    {
                        iBG.Cvalues[$"{i}C_stroke"] = state.displaySetup[$"c_{i}0_value"];
                        iBG.Cvalues[$"{i}C_opacity"] = (0.5 - (k - d_t1) * 0.5 / (d_t2 - d_t1)).ToString("0.##", CultureInfo.InvariantCulture);
                        iBG.Cvalues[$"{i}Y_stroke"] = state.displaySetup[$"c_{i}2_value"];
                        iBG.Cvalues[$"{i}Y_opacity"] = (0.5 + (k - d_t1) * 0.25 / (d_t2 - d_t1)).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                    else if (d_t3 is not double.NaN && k <= d_t3)
                    {
                        iBG.Cvalues[$"{i}Y_stroke"] = state.displaySetup[$"c_{i}2_value"];
                        iBG.Cvalues[$"{i}Y_opacity"] = (0.75 - (k - d_t2) * 0.25 / (d_t3 - d_t2)).ToString("0.##", CultureInfo.InvariantCulture);
                        iBG.Cvalues[$"{i}M_stroke"] = state.displaySetup[$"c_{i}4_value"];
                        iBG.Cvalues[$"{i}M_opacity"] = ((k - d_t2) * 0.5 / (d_t3 - d_t2)).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                    else if (d_t4 is not double.NaN && k <= d_t4)
                    {
                        iBG.Cvalues[$"{i}Y_stroke"] = state.displaySetup[$"c_{i}2_value"];
                        iBG.Cvalues[$"{i}Y_opacity"] = (0.5 - (k - d_t3) * 0.5 / (d_t4 - d_t3)).ToString("0.##", CultureInfo.InvariantCulture);
                        iBG.Cvalues[$"{i}M_stroke"] = state.displaySetup[$"c_{i}4_value"];
                        iBG.Cvalues[$"{i}M_opacity"] = (0.5 + (k - d_t3) * 0.25 / (d_t4 - d_t3)).ToString("0.##", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        iBG.Cvalues[$"{i}M_stroke"] = state.displaySetup[$"c_{i}4_value"];
                        iBG.Cvalues[$"{i}M_opacity"] = "0.75";
                    }
                }
            }
        }
    }
}
