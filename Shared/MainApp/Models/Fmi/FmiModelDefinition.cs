namespace FRAMLab.Shared
{
    /// <summary>
    /// The parsed model-level FMI definition from the &lt;FMI&gt; section.
    /// </summary>
    public sealed class FmiModelDefinition
    {
        public List<FmiVariableDefinition> Variables { get; } = new();
        public List<FmiMacroDefinition> Macros { get; } = new();
        public List<FmiDiagnostic> LoadDiagnostics { get; } = new();

        /// <summary>True when the source document had a &lt;FMI&gt;&lt;Macros&gt; node at load time.
        /// Lets the save path keep a legacy file byte-identical by not creating the node when no macros exist.</summary>
        public bool MacrosNodeExistedAtLoad { get; set; }
    }
}
