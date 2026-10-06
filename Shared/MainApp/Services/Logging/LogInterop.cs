using Microsoft.JSInterop;

namespace FMV_OG.Shared.MainApp.Services.Logging
{
    /// <summary>
    /// JS-callable bridge for the log buffer. Used by <c>wwwroot/logging.js</c> to back
    /// <c>window.FRAMLab.logs()</c> and <c>window.FRAMLab.dumpLogs()</c>.
    /// </summary>
    public static class LogInterop
    {
        /// <summary>The ring buffer, assigned once in <c>Program.cs</c> (DEBUG builds).</summary>
        public static LogStore? Store { get; set; }

        /// <summary>Returns the buffered log history as plain text.</summary>
        [JSInvokable]
        public static string DumpLogs() =>
            Store?.ToText() ?? "# FRAMLab log store is not available in this build.";
    }
}
