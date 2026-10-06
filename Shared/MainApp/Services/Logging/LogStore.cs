using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services.Logging
{
    /// <summary>A single captured log record, stored so it can be exported later.</summary>
    public sealed class LogEntry
    {
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
        public LogLevel Level { get; init; }
        public string Category { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;

        /// <summary>Full exception (type, message and stack trace), or null.</summary>
        public string? Exception { get; init; }
    }

    /// <summary>
    /// Thread-safe in-memory ring buffer of the most recent log records. The Blazor WASM
    /// console logger still writes everything to the browser devtools console; this store
    /// exists so the recent history can be exported on demand
    /// (see <c>window.FRAMLab.dumpLogs()</c>) instead of scrolling the console.
    /// </summary>
    public sealed class LogStore
    {
        private readonly object _gate = new();
        private readonly Queue<LogEntry> _entries;
        private readonly int _capacity;

        public LogStore(int capacity = 500)
        {
            _capacity = Math.Max(1, capacity);
            _entries = new Queue<LogEntry>(_capacity);
        }

        public int Capacity => _capacity;

        public int Count
        {
            get { lock (_gate) { return _entries.Count; } }
        }

        public void Add(LogEntry entry)
        {
            lock (_gate)
            {
                while (_entries.Count >= _capacity)
                {
                    _entries.Dequeue();
                }
                _entries.Enqueue(entry);
            }
        }

        public IReadOnlyList<LogEntry> Snapshot()
        {
            lock (_gate) { return _entries.ToArray(); }
        }

        public void Clear()
        {
            lock (_gate) { _entries.Clear(); }
        }

        /// <summary>Render the buffered entries as plain text (used by the export helper).</summary>
        public string ToText()
        {
            var snapshot = Snapshot();
            var sb = new StringBuilder();
            sb.Append("# FRAMLab log dump - ")
              .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
              .Append("  (")
              .Append(snapshot.Count)
              .Append(" entries, capacity ")
              .Append(_capacity)
              .AppendLine(")");

            foreach (var e in snapshot)
            {
                sb.Append(e.Timestamp.ToString("HH:mm:ss.fff"))
                  .Append(' ')
                  .Append(ShortLevel(e.Level))
                  .Append(' ')
                  .Append(e.Category)
                  .Append(" - ")
                  .AppendLine(e.Message);

                if (!string.IsNullOrEmpty(e.Exception))
                {
                    sb.AppendLine(e.Exception);
                }
            }

            return sb.ToString();
        }

        private static string ShortLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "---",
        };
    }
}
