using System;
using System.Threading.Tasks;
using FMV_OG.Shared.MainApp.Services.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Views.Components
{
    /// <summary>
    /// Drop-in replacement for <see cref="ErrorBoundary"/> that writes the unhandled
    /// exception to <see cref="ILogger"/> before showing the fallback UI, so a UI crash
    /// leaves a trace in the console instead of only "An unhandled error has occurred".
    /// </summary>
    public sealed class LoggingErrorBoundary : ErrorBoundary
    {
        [Inject]
        private ILogger<LoggingErrorBoundary> Logger { get; set; } = default!;

        protected override Task OnErrorAsync(Exception exception)
        {
            Logger.LogError(LogEvents.Ui, exception, "Unhandled exception in the component tree.");
            return Task.CompletedTask;
        }
    }
}
