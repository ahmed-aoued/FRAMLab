namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// In-memory, scope-aware lookup over the model's macros. Keyed by (Name, FunctionId);
    /// a function-scoped macro is found first, then a global (FunctionId="") macro of the same name.
    /// </summary>
    public interface IMacroRegistry
    {
        /// <summary>All macros in load order (used for the editor symbol list).</summary>
        IReadOnlyCollection<FmiMacroDefinition> All { get; }

        /// <summary>
        /// Resolves <paramref name="name"/> from <paramref name="functionId"/>: the exact-scoped macro
        /// wins, then the global fallback. Returns false when no visible macro exists.
        /// </summary>
        bool TryGetMacro(string name, string functionId, out FmiMacroDefinition macro);

        /// <summary>True when the macro sits on a call-graph cycle and must not be expanded.</summary>
        bool IsPoisoned(FmiMacroDefinition macro);

        /// <summary>Replaces the whole registry from a loaded model and re-runs static validation.</summary>
        void LoadFrom(FmiModelDefinition definition);

        /// <summary>
        /// Adds or replaces the macro by (Name, FunctionId). Returns false when the name is already used
        /// by a macro in a different scope (no shadowing), leaving the registry unchanged.
        /// </summary>
        bool Upsert(FmiMacroDefinition macro);

        bool Remove(string name, string functionId);

        /// <summary>Editor symbols visible from <paramref name="functionId"/> ("" returns globals only).</summary>
        IEnumerable<MacroSymbol> GetSymbols(string functionId = "");

        event Action? Changed;
    }
}
