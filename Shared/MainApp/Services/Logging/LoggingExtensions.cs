using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services.Logging
{
    public static class LoggingExtensions
    {
        /// <summary>
        /// Register the in-memory ring buffer used by the <c>window.FRAMLab.dumpLogs()</c>
        /// export helper. Call from <c>Program.cs</c> in DEBUG builds only.
        /// </summary>
        public static ILoggingBuilder AddFramLabLogStore(this ILoggingBuilder logging)
        {
            logging.Services.AddSingleton<LogStore>();
            logging.Services.AddSingleton<ILoggerProvider, InMemoryLoggerProvider>();
            return logging;
        }
    }
}
