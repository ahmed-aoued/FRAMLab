namespace FMV_OG.Shared.MainApp.Services.Scripting
{
    /// <summary>A single compiler/runtime diagnostic with severity and source span.</summary>
    public sealed class ScriptDiagnostic
    {
        public string Severity { get; }   // "Error" | "Warning" | "Info"
        public string Message { get; }
        public int StartLine { get; }
        public int StartColumn { get; }
        public int EndLine { get; }
        public int EndColumn { get; }

        public ScriptDiagnostic(string severity, string message, int startLine, int startColumn, int endLine, int endColumn)
        {
            Severity = severity; Message = message;
            StartLine = startLine; StartColumn = startColumn; EndLine = endLine; EndColumn = endColumn;
        }

        public static ScriptDiagnostic Error(string msg, int line, int col) =>
            new("Error", msg, line, col, line, col + 1);
        public static ScriptDiagnostic Warning(string msg, int line, int col) =>
            new("Warning", msg, line, col, line, col + 1);

        public override string ToString() => $"{Severity} {StartLine}:{StartColumn} {Message}";
    }
}
