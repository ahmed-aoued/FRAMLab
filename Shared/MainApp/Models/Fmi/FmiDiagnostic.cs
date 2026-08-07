namespace FRAMLab.Shared
{
    public enum FmiDiagnosticSeverity { Info, Warning, Error }

    /// <summary>
    /// A validation or evaluation message produced by the FMI runtime.
    /// Source examples: "Fn 3 / key Risk_Score", "FMI/Variables/Variable[EscalationCount]".
    /// </summary>
    public sealed record FmiDiagnostic(FmiDiagnosticSeverity Severity, string Source, string Message);
}
