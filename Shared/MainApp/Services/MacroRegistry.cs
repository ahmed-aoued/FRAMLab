using System.Collections.Concurrent;

namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// Thread-safe, scope-aware macro registry keyed by (Name, FunctionId). A function-scoped macro
    /// shadows nothing: names are globally unique, so <see cref="Upsert"/> rejects a same-name macro
    /// in any other scope. Lookup falls back from the exact function scope to the global ("") scope.
    /// </summary>
    public sealed class MacroRegistry : IMacroRegistry
    {
        private readonly ConcurrentDictionary<(string Name, string FunctionId), FmiMacroDefinition> _macros = new();
        private readonly ConcurrentDictionary<(string Name, string FunctionId), byte> _poisoned = new();
        private readonly List<FmiDiagnostic> _diagnostics = new();

        public IReadOnlyCollection<FmiMacroDefinition> All => _macros.Values.ToList();

        public IReadOnlyList<FmiDiagnostic> Diagnostics => _diagnostics;

        public event Action? Changed;

        public bool TryGetMacro(string name, string functionId, out FmiMacroDefinition macro)
        {
            if (_macros.TryGetValue((name, functionId), out macro!))
            {
                return true;
            }
            if (functionId != "" && _macros.TryGetValue((name, ""), out macro!))
            {
                return true;
            }
            macro = null!;
            return false;
        }

        public bool IsPoisoned(FmiMacroDefinition macro) => _poisoned.ContainsKey((macro.Name, macro.FunctionId));

        public void LoadFrom(FmiModelDefinition definition)
        {
            _macros.Clear();
            _poisoned.Clear();
            _diagnostics.Clear();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var macro in definition.Macros)
            {
                if (!seen.Add(macro.Name))
                {
                    var duplicate = new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                        $"Duplicate macro name '{macro.Name}'. Macro names must be unique across all scopes; this definition is rejected.");
                    _diagnostics.Add(duplicate);
                    definition.LoadDiagnostics.Add(duplicate);
                    continue;
                }
                _macros[(macro.Name, macro.FunctionId)] = macro;
            }
            RebuildValidation(definition.LoadDiagnostics);
            Changed?.Invoke();
        }

        public bool Upsert(FmiMacroDefinition macro)
        {
            if (!FmiMacroDefinition.NamePattern.IsMatch(macro.Name))
            {
                _diagnostics.Clear();
                _diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, "macro",
                    "Macro name must match [a-z][A-Za-z0-9_]*."));
                return false;
            }
            var key = (macro.Name, macro.FunctionId);
            if (_macros.ContainsKey(key))
            {
                _macros[key] = macro;
            }
            else if (_macros.Keys.Any(k => k.Name == macro.Name))
            {
                _diagnostics.Clear();
                _diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}",
                    $"Macro name '{macro.Name}' is already used in another scope. Macro names must be unique across all scopes."));
                return false;
            }
            else
            {
                _macros[key] = macro;
            }
            RebuildValidation(null);
            Changed?.Invoke();
            return true;
        }

        public bool Remove(string name, string functionId)
        {
            if (!_macros.TryRemove((name, functionId), out _))
            {
                return false;
            }
            RebuildValidation(null);
            Changed?.Invoke();
            return true;
        }

        public IEnumerable<MacroSymbol> GetSymbols(string functionId = "")
        {
            return _macros.Values
                .Where(m => m.FunctionId == "" || m.FunctionId == functionId)
                .OrderBy(m => m.Name)
                .Select(m =>
                {
                    var parameters = m.Parameters
                        .Select(p => new MacroParameterSymbol(p.Name, p.Type, p.Description))
                        .ToList();
                    var returnType = m.ReturnType;
                    return new MacroSymbol(m.Name, parameters, returnType, m.FunctionId, m.Description,
                        MacroSymbol.BuildSignature(m.Name, parameters, returnType));
                })
                .ToList();
        }

        private void RebuildValidation(List<FmiDiagnostic>? sink)
        {
            _poisoned.Clear();
            _diagnostics.Clear();

            //Unknown-callee / visibility scan over every macro body.
            foreach (var macro in _macros.Values)
            {
                foreach (var callee in MacroExpander.FindCallCallees(macro.Body))
                {
                    if (MacroExpander.IsBuiltIn(callee) || TryGetMacro(callee, macro.FunctionId, out _))
                    {
                        continue;
                    }
                    var knownElsewhere = _macros.Keys.Any(k => k.Name == callee);
                    var message = knownElsewhere
                        ? $"Macro '{macro.Name}' calls '{callee}', which is not visible from function '{macro.FunctionId}'."
                        : $"Macro '{macro.Name}' calls unknown macro '{callee}'.";
                    Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{macro.Name}", message), sink);
                }
            }

            //Call-graph cycle detection over the same scope-aware resolution.
            var state = new Dictionary<(string, string), int>();
            var stack = new List<(string, string)>();
            foreach (var key in _macros.Keys)
            {
                if (state.GetValueOrDefault(key) == 0)
                {
                    DetectCycle(key, state, stack);
                }
            }
            foreach (var key in _poisoned.Keys.OrderBy(k => k.Name))
            {
                Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, $"macro:{key.Name}",
                    $"Macro '{key.Name}' is part of a circular macro reference."), sink);
            }
        }

        private void DetectCycle((string Name, string FunctionId) key, Dictionary<(string, string), int> state, List<(string, string)> stack)
        {
            state[key] = 1;
            stack.Add(key);
            if (_macros.TryGetValue(key, out var macro))
            {
                foreach (var callee in MacroExpander.FindCallCallees(macro.Body))
                {
                    if (!TryGetMacro(callee, macro.FunctionId, out var target))
                    {
                        continue;
                    }
                    var targetKey = (target.Name, target.FunctionId);
                    if (targetKey == key)
                    {
                        _poisoned[key] = 0;
                        continue;
                    }
                    if (state.GetValueOrDefault(targetKey) == 0)
                    {
                        DetectCycle(targetKey, state, stack);
                    }
                    else if (state[targetKey] == 1)
                    {
                        var index = stack.IndexOf(targetKey);
                        for (var i = index; i < stack.Count; i++)
                        {
                            _poisoned[stack[i]] = 0;
                        }
                    }
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[key] = 2;
        }

        private void Add(FmiDiagnostic diagnostic, List<FmiDiagnostic>? sink)
        {
            _diagnostics.Add(diagnostic);
            sink?.Add(diagnostic);
        }
    }
}
