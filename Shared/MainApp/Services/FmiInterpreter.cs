using System.Text.RegularExpressions;

namespace FMV_OG.Shared.MainApp.Services
{
    public class FmiInterpreter : IFmiInterpreter
    {
        private readonly IExpressionEvaluator expressionEvaluator;
        private readonly IMetadataService metadataService;
        private static readonly Regex IncomingVariablePattern = new(@"^([IPRCT]\d+_|[IPRCTA](n|sum|avg|min|max|prd)_)");

        //Optional: inject Prev_<Key> variables from PreviousCycleValues into equations. Off this phase.
        public bool EnablePrevVariables { get; set; } = false;

        public FmiRunState RunState { get; private set; } = new();
        public FmiModelDefinition Definition { get; private set; } = new();

        public FmiInterpreter(IExpressionEvaluator expressionEvaluator, IMetadataService metadataService)
        {
            this.expressionEvaluator = expressionEvaluator;
            this.metadataService = metadataService;
        }

        public void Initialize(FmiModelDefinition definition, List<Function> functions)
        {
            Definition = definition;
            RunState = new FmiRunState();
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
                        InjectVariable(fn, $"Prev_{prev.Key}", prev.Value, "");
                    }
                }
            }
        }

        private static void InjectVariable(Function fn, string name, FmiValue value, string description)
        {
            var legacy = value.ToLegacyString();
            fn.newVariable(name, new string[] { legacy, description != "" ? description : legacy });
        }

        public FmiEvaluationResult EvaluateFunction(Function iBG, FmiEvaluationContext ctx)
        {
            var result = new FmiEvaluationResult { FunctionId = iBG.IDNr };
            metadataService.CalcVariables(iBG, ctx.CouplingList, ctx.FunctionList, ctx.Cycle); //calculate the new variable values for the given function
            if (iBG.metadata is not null)
            {
                foreach (string[] md3Key in iBG.metadata) //[0]=key name, [1]=key value, [2]=active attribute, [3]=equation attribute
                {
                    if (md3Key[1].Length > 0 || md3Key[3].Length > 0) //metadata key has a value or has an equation
                    {
                        if (md3Key[2] == "true") //it has an equation
                        {
                            if (md3Key[0].StartsWith("Global_"))
                            {
                                iBG.newVariable(md3Key[0], new string[] { metadataService.MetaDataKeys[md3Key[0]], metadataService.MetaDataKeys[md3Key[0]] });
                            }
                            else
                            {
                                iBG.newVariable(md3Key[0], new string[] { md3Key[1], md3Key[1] }); //if equation, update variable before, and again after, the eval
                                metadataService.MetaDataKeys[md3Key[0]] = md3Key[1];
                            }
                            //=======================================
                            //This is an option to run custom code on a Function. The code is written in the Function.cs component. An example is 'returnTrue'
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
                            //=======================================
                            //This is an option to call an external API
                            // if (md3Key[3].StartsWith("api(")) //check for api use here, otherwise pass to equation eval function
                            // {
                            //     var apiExpression = md3Key[3].Replace("api(", "").Replace(")", "").Replace("\"", "").Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            //     var jsonContent = "";
                            //     var varCount = apiExpression.Length;
                            //     var varParameters = $"?fnName={iBG.IDName}&keyName={md3Key[0]}";
                            //     if (varCount > 1) //if more than one parameter in api() call, then build a query string, otherwise add all variables and data to the body content
                            //     {
                            //         for (var i = 1; i < varCount; i++)
                            //         {
                            //             varParameters += $"&{apiExpression[i]}={iBG.getVariable(apiExpression[i])?[0]??""}";
                            //         }
                            //         jsonContent = "";
                            //     }
                            //     else
                            //     {
                            //         jsonContent = JsonSerializer.Serialize(iBG.getVariables());
                            //     }
                            //     var requestUrl = new Uri($"{apiExpression[0]}{varParameters}");
                            //     var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                            //     requestMessage.Content = new StringContent(jsonContent);
                            //     var reply = await Http.SendAsync(requestMessage);
                            //     md3Key[1] = reply.StatusCode == HttpStatusCode.OK ? await reply.Content.ReadAsStringAsync() : reply?.ReasonPhrase ?? "NaN";
                            // }
                            //=======================================
                            else
                            {
                                try
                                {
                                    md3Key[1] = expressionEvaluator.Eval(md3Key[3], iBG.getVariables(), ctx.DataRows, ctx.DataKeys);
                                }
                                catch (Exception ex)
                                {
                                    result.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"Fn {iBG.IDNr} / key {md3Key[0]}", ex.Message));
                                    throw;
                                }
                            }
                        }
                        iBG.newVariable(md3Key[0], new string[] { md3Key[1], md3Key[1] });
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

        /// <summary>Legacy -> RunState mirror after evaluating one function (legacy stays authoritative).</summary>
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
            if (fn.metadata is not null)
            {
                foreach (var md in fn.metadata)
                {
                    declaredByName.TryGetValue(md[0], out var def);
                    var value = FmiValue.FromLegacyString(md[1], def?.Type, out var coerced);
                    state.OutputValues[md[0]] = value;
                    if (def is not null)
                    {
                        if (!coerced)
                        {
                            result.Diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, $"Fn {fn.IDNr} / key {md[0]}",
                                $"Value '{md[1]}' does not match declared type '{def.Type}'."));
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
                }
            }
            foreach (var variable in fn.getVariables())
            {
                if (IncomingVariablePattern.IsMatch(variable.Key))
                {
                    state.IncomingValues[variable.Key] = FmiValue.FromLegacyString(variable.Value[0]);
                }
                else if (declaredByName.TryGetValue(variable.Key, out var def) && def.Persist && def.Scope == FmiVariableScope.Function)
                {
                    state.PersistentVariables[def.Name] = FmiValue.FromLegacyString(variable.Value[0], def.Type);
                }
            }
            foreach (var globalKey in metadataService.MetaDataKeys)
            {
                if (globalKey.Key.StartsWith("Global_"))
                {
                    RunState.GlobalVariables[globalKey.Key] = FmiValue.FromLegacyString(globalKey.Value);
                }
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
                state.PreviousCycleValues.Clear();
                foreach (var output in state.OutputValues)
                {
                    state.PreviousCycleValues[output.Key] = output.Value;
                }
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

        public string EvaluateExpression(string expression, Dictionary<string, string[]> variables, FmiEvaluationContext ctx)
        {
            return expressionEvaluator.Eval(expression, variables, ctx.DataRows, ctx.DataKeys);
        }
    }
}
