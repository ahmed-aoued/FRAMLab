using System;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services.Logging
{
    /// <summary>
    /// Logger provider that mirrors every log record into a <see cref="LogStore"/> ring
    /// buffer. It is registered in DEBUG builds only; the Blazor WASM console logger
    /// always remains in the pipeline, so the browser console is still the primary view.
    /// </summary>
    public sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        private readonly LogStore _store;

        public InMemoryLoggerProvider(LogStore store) => _store = store;

        public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_store, categoryName);

        public void Dispose()
        {
            // Nothing to dispose: the LogStore lives for the lifetime of the app.
        }

        private sealed class InMemoryLogger : ILogger
        {
            private readonly LogStore _store;
            private readonly string _category;

            public InMemoryLogger(LogStore store, string category)
            {
                _store = store;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            // The logging pipeline applies the configured filters before invoking the
            // provider, so accept everything that reaches us.
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter is null) return;

                _store.Add(new LogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Level = logLevel,
                    Category = _category,
                    Message = formatter(state, exception),
                    Exception = exception?.ToString(),
                });
            }
        }
    }
}
