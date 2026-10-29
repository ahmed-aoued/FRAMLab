namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// What a model uses beyond plain metadata equations: ST scripts, macro calls, declared (typed)
    /// FMI variables and script globals. Drives the canvas badges, the FMI panel summary and the
    /// FMI Setup overview, so users can see at a glance where behaviour comes from.
    /// </summary>
    public sealed class FmiModelUsage
    {
        /// <summary>Functions with an enabled, non-empty ST script.</summary>
        public HashSet<string> ScriptFunctions { get; } = new();
        /// <summary>Function id → (metadata key → macro names its active equation calls).</summary>
        public Dictionary<string, Dictionary<string, List<string>>> MacroCalls { get; } = new();
        public int MacroCount { get; init; }
        public int DeclaredVariableCount { get; init; }
        public int ScriptGlobalCount { get; init; }
        public int DataSourceCount { get; init; }
        /// <summary>Function id → data source names it reads with api().</summary>
        public Dictionary<string, HashSet<string>> ApiCalls { get; } = new();
        public bool ReadsData(string functionId) => ApiCalls.ContainsKey(functionId);

        public bool UsesScripts => ScriptFunctions.Count > 0;
        public bool UsesMacros => MacroCalls.Count > 0 || MacroCount > 0;
        public bool HasScript(string functionId) => ScriptFunctions.Contains(functionId);
        public bool CallsMacros(string functionId) => MacroCalls.ContainsKey(functionId);

        /// <summary>Hover text for a function's badges, e.g. "ST script · macros: Risk ← clamp01".</summary>
        public string Describe(string functionId)
        {
            var parts = new List<string>();
            if (HasScript(functionId)) parts.Add("ST script runs after the equations");
            if (MacroCalls.TryGetValue(functionId, out var calls))
            {
                parts.Add("macros: " + string.Join(", ", calls.Select(kv => $"{kv.Key} ← {string.Join("/", kv.Value)}")));
            }
            if (ApiCalls.TryGetValue(functionId, out var sources))
            {
                parts.Add("live data: " + string.Join(", ", sources));
            }
            return string.Join(" · ", parts);
        }

        public static FmiModelUsage Analyze(IEnumerable<Function> functions, FmiModelDefinition definition, int scriptGlobalCount)
        {
            var usage = new FmiModelUsage
            {
                MacroCount = definition.Macros.Count,
                DeclaredVariableCount = definition.Variables.Count(v => !v.IsImplicit),
                ScriptGlobalCount = scriptGlobalCount,
                DataSourceCount = definition.DataSources.Count,
            };
            foreach (var fn in functions)
            {
                var read = FmiDataService.SourcesUsedBy(fn);
                if (read.Count > 0) usage.ApiCalls[fn.IDNr] = read;
                if (fn.Script is { Enabled: true } script && !string.IsNullOrWhiteSpace(script.Source))
                {
                    usage.ScriptFunctions.Add(fn.IDNr);
                }
                if (definition.Macros.Count == 0 || fn.metadata is null) continue;

                // a macro is visible to a function when it is global or scoped to that function
                var visible = definition.Macros
                    .Where(m => m.FunctionId == "" || m.FunctionId == fn.IDNr)
                    .Select(m => m.Name)
                    .ToHashSet();
                foreach (var md in fn.metadata)
                {
                    if (md[2] != "true" || string.IsNullOrWhiteSpace(md[3])) continue;
                    var called = MacroExpander.FindCallCallees(md[3]).Where(visible.Contains).Distinct().ToList();
                    if (called.Count == 0) continue;
                    if (!usage.MacroCalls.TryGetValue(fn.IDNr, out var byKey))
                    {
                        byKey = new Dictionary<string, List<string>>();
                        usage.MacroCalls[fn.IDNr] = byKey;
                    }
                    byKey[md[0]] = called;
                }
            }
            return usage;
        }
    }
}
