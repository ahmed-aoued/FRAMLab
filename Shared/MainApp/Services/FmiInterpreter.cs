using System.Globalization;
using System.Text.RegularExpressions;
//To change:
//EvaluateExpression()
//


namespace FRAMLab.Shared.MainApp.Services
{
    public class FmiInterpreter : IFmiInterpreter
    {
        private readonly IExpressionEvaluator expressionEvaluator;
        private readonly IMetadataService metadataService;
        private readonly IMacroRegistry macroRegistry;
        private readonly MacroExpander macroExpander;
        private static readonly Regex IncomingVariablePattern = new(@"^([IPRCT]\d+_|[IPRCTA](n|sum|avg|min|max|prd)_)");
        private static readonly Regex MacroCalleePattern = new(@"([a-z][A-Za-z0-9_]*)\s*\(");

        public bool EnablePrevVariables { get; set; } = false;

        /// <summary>
        /// Diagnostics raised outside <see cref="EvaluateFunction"/> (currently <see cref="BeginCycle"/>
        /// Prev_* name collisions). Callers (StateContainer/UI) drain this after each lifecycle call.
        /// </summary>
        public List<FmiDiagnostic> PendingDiagnostics { get; } = new();

        public FmiRunState RunState { get; private set; } = new();
        public FmiModelDefinition Definition { get; private set; } = new();

        public FmiInterpreter(IExpressionEvaluator expressionEvaluator, IMetadataService metadataService)
            : this(expressionEvaluator, metadataService, new MacroRegistry())
        {
        }

        public FmiInterpreter(IExpressionEvaluator expressionEvaluator, IMetadataService metadataService, IMacroRegistry macroRegistry)
        {
            this.expressionEvaluator = expressionEvaluator;
            this.metadataService = metadataService;
            this.macroRegistry = macroRegistry;
            macroExpander = new MacroExpander(macroRegistry);
        }

        public void Initialize(FmiModelDefinition definition, List<Function> functions)
        {
            Definition = definition;
            RunState = new FmiRunState();
            macroRegistry.LoadFrom(definition);
            Reset(functions);
        }

        public void OnFunctionAdded(Function fn)
        {
            var state = RunState.GetOrCreate(fn.IDNr);
            SeedFunctionState(state);
        }

        public void Reset(List<Function> functions)
        {
            RunState.Cycle = -1;
            RunState.History.Clear();
            RunState.ActiveCouplings.Clear();
            RunState.GlobalVariables.Clear();
            foreach (var def in Definition.Variables)
            {
                if (def.Scope == FmiVariableScope.Model)
                {
                    RunState.GlobalVariables[def.Name] = def.InitialValue;
                }
            }
            foreach (var fn in functions)
            {
                var state = RunState.GetOrCreate(fn.IDNr);
                state.Clear();
                SeedFunctionState(state);
            }
            //Drop stale Global_* runtime values (re-seeded on the next EvaluateFunction pass)
            //without removing the key registrations, which the legacy Global_ read path indexes directly.
            foreach (var key in metadataService.MetaDataKeys.Keys.Where(k => k.StartsWith("Global_")).ToList())
            {
                metadataService.MetaDataKeys[key] = "";
            }
        }

        private void SeedFunctionState(FmiFunctionState state)
        {
            foreach (var def in Definition.Variables)
            {
                if (def.Scope == FmiVariableScope.Function && AppliesTo(def, state.FunctionId))
                {
                    state.PersistentVariables[def.Name] = def.InitialValue;
                }
            }
        }

        private static bool AppliesTo(FmiVariableDefinition def, string functionId)
        {
            return def.FunctionId == "" || def.FunctionId == functionId;
        }

        public void BeginCycle(FmiEvaluationContext ctx)
        {
            RunState.Cycle = ctx.Cycle;
            foreach (var fn in ctx.FunctionList)
            {
                var state = RunState.GetOrCreate(fn.IDNr);
                state.WasActivatedThisCycle = false;
                foreach (var def in Definition.Variables)
                {
                    switch (def.Scope)
                    {
                        case FmiVariableScope.Cycle when AppliesTo(def, fn.IDNr):
                            state.CycleVariables[def.Name] = def.InitialValue;
                            InjectVariable(fn, def.Name, def.InitialValue, def.Description);
                            break;
                        case FmiVariableScope.Function when AppliesTo(def, fn.IDNr):
                            if (state.PersistentVariables.TryGetValue(def.Name, out var persisted))
                            {
                                InjectVariable(fn, def.Name, persisted, def.Description);
                            }
                            break;
                        case FmiVariableScope.Model:
                            if (RunState.GlobalVariables.TryGetValue(def.Name, out var global))
                            {
                                InjectVariable(fn, def.Name, global, def.Description);
                            }
                            break;
                    }
                }
                if (EnablePrevVariables)
                {
                    foreach (var prev in state.PreviousCycleValues)
                    {
                        var prevName = $"Prev_{prev.Key}";
                        var collision = Definition.Variables.Any(def => def.Name == prevName && AppliesTo(def, fn.IDNr));
                        if (collision)
                        {
                            PendingDiagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Info, $"Fn {fn.IDNr} / key {prevName}",
                                $"Prev_* variable '{prevName}' collides with a declared variable of the same name; the declared variable is authoritative."));
                        }
                        InjectVariable(fn, prevName, prev.Value, "");
                    }
                }
            }
        }

        private static variableType MapType(FmiValueType kind) => kind switch
        {
            FmiValueType.Number => variableType.Double,
            FmiValueType.Boolean => variableType.Bool,
            FmiValueType.Array => variableType.Array1D,
            _ => variableType.String
        };

        private static variableType InferType(string raw)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return variableType.Double;
            if (raw == "true" || raw == "false" || raw == "1" || raw == "0")
                return variableType.Bool;
            return variableType.String;
        }

        private static object CoerceValue(string raw, variableType type) => type switch
        {
            variableType.Double => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0,
            variableType.Int => int.TryParse(raw, out var i) ? i : 0,
            variableType.Bool => raw == "true" || raw == "1",
            _ => raw
        };

        private static void InjectVariable(Function fn, string name, FmiValue value, string description)
        {
            var type = MapType(value.Type);
            var v = fn.getVariable(name) ?? fn.newVariable(name, type);
            v.Value = value.Type switch
            {
                FmiValueType.Number => value.AsNumber(),
                FmiValueType.Boolean => value.AsBool(),
                FmiValueType.Array => value.AsList(),
                _ => value.ToLegacyString()
            };
        }

        public FmiEvaluationResult EvaluateFunction(Function iBG, FmiEvaluationContext ctx)
        {
            var result = new FmiEvaluationResult { FunctionId = iBG.IDNr };
            metadataService.CalcVariables(iBG, ctx.CouplingList, ctx.FunctionList, ctx.Cycle);
            if (iBG.metadata is not null)
            {
                foreach (string[] md3Key in iBG.metadata)
                {
                    if (md3Key[1].Length > 0 || md3Key[3].Length > 0)
                    {
                        if (md3Key[2] == "true")
                        {
                            if (md3Key[0].StartsWith("Global_"))
                            {
                                var vg = iBG.getVariable(md3Key[0]) ?? iBG.newVariable(md3Key[0], variableType.String);
                                vg.Value = metadataService.MetaDataKeys[md3Key[0]];
                            }
                            else
                            {
                                var va = iBG.getVariable(md3Key[0]) ?? iBG.newVariable(md3Key[0], variableType.String);
                                va.Value = md3Key[1];
                                metadataService.MetaDataKeys[md3Key[0]] = md3Key[1];
                            }
                            if (md3Key[3].StartsWith("execute("))
                            {
                                var contentText = md3Key[3].Replace("execute(", "").Replace(")", "");
                                var contentArray = contentText.Split(",");
                                var codeName = contentArray[0];
                                var method = iBG.GetType().GetMethod(codeName);
                                if (method is not null)
                                {
                                    contentArray[0] = md3Key[0];
                                    md3Key[1] = method.Invoke(iBG, new object[] { contentArray })?.ToString() ?? "STOP";
                                }
                            }
                            else if (md3Key[3].StartsWith("array("))
                            {
                                var content = $"{md3Key[0]},{md3Key[3].Replace("array(", "").Replace(")", "")}".Split(",");
                                md3Key[1] = iBG.array(content);
                            }
                            else
                            {
                                try
                                {
                                    var vars = iBG.variableList.ToDictionary(v => v.Name, v => new string[] { v.Value?.ToString() ?? "", "" });
                                    md3Key[1] = expressionEvaluator.Eval(md3Key[3], vars, ctx.DataRows, ctx.DataKeys);
                                }
                                catch (Exception ex)
                                {
                                    result.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"Fn {iBG.IDNr} / key {md3Key[0]}", ex.Message));
                                    throw;
                                }
                            }
                        }
                        var vr = iBG.getVariable(md3Key[0]);
                        if (vr == null) { vr = iBG.newVariable(md3Key[0], InferType(md3Key[1])); }
                        else if (vr.Type == variableType.String) { vr.Type = InferType(md3Key[1]); }
                        vr.Value = CoerceValue(md3Key[1], vr.Type);
                        metadataService.MetaDataKeys[md3Key[0]] = md3Key[1];

                        var fnMD = metadataService.MetadataDS.Tables["Metadata"]!.Select($"IDNr = {iBG.IDNr} and Cycle = -1")[0];
                        if (fnMD is not null && fnMD[md3Key[0]] is not null)
                        {
                            fnMD[md3Key[0]] = md3Key[1];
                        }
                        result.EvaluatedKeys.Add((md3Key[0], md3Key[1]));
                    }
                }
            }
            MirrorFunctionState(iBG, result);
            return result;
        }

        private void MirrorFunctionState(Function fn, FmiEvaluationResult result)
        {
            var state = RunState.GetOrCreate(fn.IDNr);
            var declaredByName = new Dictionary<string, FmiVariableDefinition>();
            foreach (var def in Definition.Variables)
            {
                if (AppliesTo(def, fn.IDNr))
                {
                    declaredByName[def.Name] = def;
                }
            }
            var metadataKeys = new HashSet<string>();
            if (fn.metadata is not null)
            {
                foreach (var md in fn.metadata)
                {
                    metadataKeys.Add(md[0]);
                    declaredByName.TryGetValue(md[0], out var def);
                    WriteBackDeclared(md[0], md[1], def, state, result);
                }
            }
            foreach (var variable in fn.variableList)
            {
                var val = variable.Value?.ToString() ?? "";
                if (IncomingVariablePattern.IsMatch(variable.Name))
                {
                    state.IncomingValues[variable.Name] = FmiValue.FromLegacyString(val);
                }
                else if (!metadataKeys.Contains(variable.Name)
                    && declaredByName.TryGetValue(variable.Name, out var def) && def.Persist)
                {
                    //only for declared persist variables with no metadata row; metadata-backed keys
                    //are authoritative via the metadata loop above (the variableList entry is stale)
                    WriteBackDeclared(variable.Name, val, def, state, result);
                }
            }
            foreach (var globalKey in metadataService.MetaDataKeys)
            {
                if (globalKey.Key.StartsWith("Global_"))
                {
                    var def = FindModelDefinition(globalKey.Key);
                    var value = FmiValue.FromLegacyString(globalKey.Value, def?.Type, out var coerced);
                    if (def is not null && !coerced)
                    {
                        result.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, $"Fn {fn.IDNr} / key {globalKey.Key}",
                            $"Value '{globalKey.Value}' does not match declared type '{def.Type}'."));
                    }
                    RunState.GlobalVariables[globalKey.Key] = value;
                }
            }
        }

        private FmiVariableDefinition? FindModelDefinition(string name)
        {
            foreach (var def in Definition.Variables)
            {
                if (def.Scope == FmiVariableScope.Model && def.Name == name)
                {
                    return def;
                }
            }
            return null;
        }

        /// <summary>
        /// Single write-back path shared by the legacy metadata loop and the variableList loop:
        /// coerces the raw legacy string against the declared type (warning diagnostic on mismatch),
        /// always mirrors into OutputValues, and routes persisted values to PersistentVariables
        /// (function scope) or GlobalVariables (model scope).
        /// </summary>
        private void WriteBackDeclared(string name, string rawValue, FmiVariableDefinition? def, FmiFunctionState state, FmiEvaluationResult result)
        {
            var value = FmiValue.FromLegacyString(rawValue, def?.Type, out var coerced);
            state.OutputValues[name] = value;
            if (def is null)
            {
                return;
            }
            if (!coerced)
            {
                result.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, $"Fn {state.FunctionId} / key {name}",
                    $"Value '{rawValue}' does not match declared type '{def.Type}'."));
            }
            if (def.Persist && def.Scope == FmiVariableScope.Function)
            {
                state.PersistentVariables[def.Name] = value;
            }
            else if (def.Persist && def.Scope == FmiVariableScope.Model)
            {
                RunState.GlobalVariables[def.Name] = value;
            }
        }

        public void EndCycle(FmiEvaluationContext ctx)
        {
            var snapshot = new FmiCycleSnapshot { Cycle = ctx.Cycle };
            foreach (var fn in ctx.FunctionList)
            {
                var state = RunState.GetOrCreate(fn.IDNr);
                state.WasActivatedThisCycle = fn.fmiHighlight != "";
                state.WasEverActive = fn.wasActive;
                if (state.WasActivatedThisCycle)
                {
                    snapshot.ActivatedFunctions.Add(fn.IDNr);
                }
                snapshot.FunctionValues[fn.IDNr] = new Dictionary<string, FmiValue>(state.OutputValues);
                snapshot.PersistentValues[fn.IDNr] = new Dictionary<string, FmiValue>(state.PersistentVariables);
                state.PreviousCycleValues.Clear();
                foreach (var output in state.OutputValues)
                {
                    state.PreviousCycleValues[output.Key] = output.Value;
                }
            }
            foreach (var global in RunState.GlobalVariables)
            {
                snapshot.GlobalValues[global.Key] = global.Value;
            }
            RunState.History.Add(snapshot);
            RunState.ActiveCouplings.Clear();
            foreach (var coupling in ctx.CouplingList)
            {
                if (coupling.activated)
                {
                    RunState.ActiveCouplings.Add(coupling.Name);
                }
            }
        }

        public string EvaluateExpression(string expression, Dictionary<string, string[]> variables, FmiEvaluationContext ctx, string callingFunctionId = "")
        {
            //No macros in the model: legacy path, byte-identical behavior for legacy files.
            if (macroRegistry.All.Count == 0)
            {
                return expressionEvaluator.Eval(expression, variables, ctx.DataRows, ctx.DataKeys);
            }

            var hasMacroCall = false;
            var unknownCallee = "";
            foreach (Match match in MacroCalleePattern.Matches(expression))
            {
                var name = match.Groups[1].Value;
                if (macroRegistry.TryGetMacro(name, callingFunctionId, out _))
                {
                    hasMacroCall = true;
                }
                else if (!MacroExpander.IsBuiltIn(name))
                {
                    unknownCallee = name;
                }
            }

            if (hasMacroCall)
            {
                return macroExpander.Expand(expression, variables, callingFunctionId, ctx, PendingDiagnostics);
            }
            if (unknownCallee != "")
            {
                PendingDiagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{unknownCallee}",
                    $"Unknown macro '{unknownCallee}'. It is not a built-in function and no macro with that name is visible here."));
                return "";
            }
            return expressionEvaluator.Eval(expression, variables, ctx.DataRows, ctx.DataKeys);
        }
    }
}
