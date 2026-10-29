namespace FRAMLab.Shared
{
    public sealed record MacroParameterSymbol(string Name, FmiTypeDescriptor Type, string Description);

    /// <summary>Editor-facing view of a macro for the SymbolTable / future Monaco integration.</summary>
    public sealed record MacroSymbol(
        string Name,
        IReadOnlyList<MacroParameterSymbol> Parameters,
        FmiTypeDescriptor? ReturnType,                    //null = inferred
        string FunctionId,                                //"" = global; editor filters by current function
        string Description,
        string Signature)                                 //pre-rendered "clamp01(x: number): number"
    {
        public static string BuildSignature(string name, IReadOnlyList<MacroParameterSymbol> parameters, FmiTypeDescriptor? returnType)
        {
            var args = string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Type}"));
            var ret = returnType is null ? "" : $": {returnType}";
            return $"{name}({args}){ret}";
        }
    }
}
