namespace FMV_OG.Shared
{
    /// <summary>
    /// The parsed model-level FMI definition from the &lt;FMI&gt; section.
    /// Rules and Macros are later phases; their XML nodes are preserved untouched on save.
    /// </summary>
    public sealed class FmiModelDefinition
    {
        public List<FmiVariableDefinition> Variables { get; } = new();
        public List<FmiDiagnostic> LoadDiagnostics { get; } = new();
    }
}
