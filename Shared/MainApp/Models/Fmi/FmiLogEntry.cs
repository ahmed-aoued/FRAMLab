namespace FRAMLab.Shared
{
    public enum FmiLogLevel { Info, Warning, Error }

    /// <summary>One line of the FMI run log, structured so the log panel can filter by level
    /// and origin and jump to the function. <see cref="Origin"/> is "Run", "Model", "Equation",
    /// "Macro" or "Script".</summary>
    public sealed record FmiLogEntry(int Cycle, FmiLogLevel Level, string Origin, string FunctionId, string FunctionName, string Message);
}
