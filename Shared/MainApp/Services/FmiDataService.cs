using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// Live data for api("source", "path") in equations and scripts. Reads only: a run never
    /// changes anything outside FRAMLab. Runs entirely in the browser: HTTP sources (a URL, a REST
    /// API, or a REST layer in front of a database) must allow CORS; WebSocket sources push JSON
    /// messages and the latest message is the value.
    ///
    /// Each function decides per source when it reads (<see cref="FmiDataTiming"/>): on activation
    /// (fetched just before its equations), live (the source's latest value at the moment of the
    /// read), or on exit (fetched after it activated; its next evaluation reads that data).
    /// </summary>
    public sealed class FmiDataService
    {
        private static readonly Regex ApiCallPattern = new(@"api\s*\(\s*[""']([A-Za-z][A-Za-z0-9_]*)[""']", RegexOptions.IgnoreCase);

        private readonly HttpClient http;
        private readonly Func<string, Task<string?>> secretProvider;
        private readonly Dictionary<string, FmiDataSource> sources = new();
        private readonly List<FmiDataUse> uses = new();
        private readonly Dictionary<string, SourceState> state = new();
        /// <summary>Per function and source: the data that function reads (OnActivation / OnExit timing).</summary>
        private readonly Dictionary<(string FunctionId, string Source), JsonNode?> snapshots = new();
        private CancellationTokenSource liveCancel = new();

        /// <param name="secretProvider">Returns the browser-stored key for a source name (null = none).</param>
        public FmiDataService(HttpClient http, Func<string, Task<string?>> secretProvider)
        {
            this.http = http;
            this.secretProvider = secretProvider;
        }

        private sealed class SourceState
        {
            public JsonNode? Latest;
            public DateTime? FetchedAt;
            public string Error = "";
            public bool LiveRunning;
        }

        /// <summary>Status shown in FMI Setup: when data last arrived, the last error, and a preview.</summary>
        public sealed record SourceStatus(DateTime? FetchedAt, string Error, string Preview);

        public IReadOnlyCollection<FmiDataSource> Sources => sources.Values;

        /// <summary>localStorage key holding a source's API key in this browser (never in the .xfmv).</summary>
        public static string SecretStorageKey(string sourceName) => $"framlab.datasource.{sourceName}.key";

        /// <summary>Apply the model's data sources and timings (on load and after edits). Stops live feeds.</summary>
        public void Configure(IEnumerable<FmiDataSource> definedSources, IEnumerable<FmiDataUse> definedUses)
        {
            StopLive();
            sources.Clear();
            foreach (var s in definedSources) sources[s.Name] = s;
            uses.Clear();
            uses.AddRange(definedUses);
            foreach (var name in state.Keys.Where(k => !sources.ContainsKey(k)).ToList()) state.Remove(name);
            snapshots.Clear();
        }

        /// <summary>FMI Reset: forget per-function data and stop live feeds (cached source data is kept for the status view).</summary>
        public void Reset()
        {
            StopLive();
            snapshots.Clear();
        }

        public FmiDataTiming TimingFor(string source, string functionId) =>
            uses.FirstOrDefault(u => u.Source == source && u.FunctionId == functionId)?.Timing ?? FmiDataTiming.OnActivation;

        /// <summary>Data source names a function reads with api(...) in its active equations or its script.</summary>
        public static HashSet<string> SourcesUsedBy(Function fn)
        {
            var names = new HashSet<string>();
            void Scan(string? text)
            {
                if (string.IsNullOrEmpty(text)) return;
                foreach (Match m in ApiCallPattern.Matches(text)) names.Add(m.Groups[1].Value);
            }
            foreach (var md in fn.metadata ?? new List<string[]>())
            {
                if (md.Length > 3 && md[2] == "true") Scan(md[3]);
            }
            if (fn.Script is { Enabled: true }) Scan(fn.Script.Source);
            return names;
        }

        /// <summary>Before a function's equations run: fetch its on-activation sources, make sure its
        /// live sources are running, and give on-exit sources a first value.</summary>
        public async Task BeforeEvaluateAsync(Function fn)
        {
            foreach (var name in SourcesUsedBy(fn).Where(sources.ContainsKey))
            {
                switch (TimingFor(name, fn.IDNr))
                {
                    case FmiDataTiming.OnActivation:
                        await FetchAsync(name);
                        snapshots[(fn.IDNr, name)] = Current(name);
                        break;
                    case FmiDataTiming.Live:
                        await EnsureLiveAsync(name);
                        break;
                    case FmiDataTiming.OnExit:
                        if (!snapshots.ContainsKey((fn.IDNr, name)))
                        {
                            await FetchAsync(name); // nothing captured at an exit yet
                            snapshots[(fn.IDNr, name)] = Current(name);
                        }
                        break;
                }
            }
        }

        /// <summary>After a function activated: capture its on-exit sources for its next evaluation.</summary>
        public async Task AfterActivateAsync(Function fn)
        {
            foreach (var name in SourcesUsedBy(fn).Where(n => sources.ContainsKey(n) && TimingFor(n, fn.IDNr) == FmiDataTiming.OnExit))
            {
                await FetchAsync(name);
                snapshots[(fn.IDNr, name)] = Current(name);
            }
        }

        /// <summary>
        /// Read one value for a function. Numbers come back in invariant culture, booleans as 1/0,
        /// text as-is. False with a reason when the source, its data or the path is missing.
        /// </summary>
        public bool TryRead(string functionId, string sourceName, string path, out string value, out string error)
        {
            value = "";
            if (!sources.ContainsKey(sourceName))
            {
                error = $"No data source named '{sourceName}'. Define it in FMI Setup → Data sources.";
                return false;
            }
            JsonNode? root;
            if (TimingFor(sourceName, functionId) == FmiDataTiming.Live)
            {
                root = Current(sourceName);
            }
            else if (!snapshots.TryGetValue((functionId, sourceName), out root))
            {
                root = Current(sourceName); // e.g. evaluated outside a run (FMI panel preview)
            }
            if (root is null)
            {
                var reason = state.TryGetValue(sourceName, out var st) && st.Error != "" ? st.Error : "no data has arrived yet";
                error = $"Data source '{sourceName}' has no data: {reason}.";
                return false;
            }
            return TryReadPath(root, path, out value, out error);
        }

        /// <summary>Fetch an HTTP source now (WebSocket sources connect instead). Errors are kept in the status.</summary>
        public async Task FetchAsync(string name)
        {
            if (!sources.TryGetValue(name, out var source)) return;
            var st = StateOf(name);
            if (source.Kind == FmiDataSourceKind.WebSocket)
            {
                await EnsureLiveAsync(name);
                return;
            }
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source.Address);
                if (source.AuthHeader != "" && await secretProvider(source.Name) is { Length: > 0 } secret)
                {
                    request.Headers.TryAddWithoutValidation(source.AuthHeader, secret);
                }
                using var response = await http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    st.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    return;
                }
                st.Latest = Parse(text, source.Format);
                st.FetchedAt = DateTime.Now;
                st.Error = "";
            }
            catch (Exception ex)
            {
                // In the browser a blocked CORS request surfaces as a generic fetch failure.
                st.Error = $"could not fetch {source.Address} ({ex.Message}). If the address works in a browser tab, the server may not allow cross-origin (CORS) requests";
            }
        }

        public SourceStatus StatusOf(string name)
        {
            if (!state.TryGetValue(name, out var st)) return new SourceStatus(null, "", "");
            var preview = st.Latest?.ToJsonString() ?? "";
            if (preview.Length > 600) preview = preview[..600] + " …";
            return new SourceStatus(st.FetchedAt, st.Error, preview);
        }

        /// <summary>Every readable path in the source's current data (for the editor's path picker).</summary>
        public List<(string Path, string Value)> ListPaths(string name, int max = 200)
        {
            var result = new List<(string, string)>();
            void Walk(JsonNode? node, string path)
            {
                if (result.Count >= max) return;
                switch (node)
                {
                    case JsonObject obj:
                        foreach (var (key, child) in obj) Walk(child, path == "" ? key : $"{path}.{key}");
                        break;
                    case JsonArray arr:
                        for (var i = 0; i < arr.Count && i < 3; i++) Walk(arr[i], path == "" ? $"{i}" : $"{path}[{i}]");
                        break;
                    default:
                        if (TryReadPath(node!, "", out var v, out _)) result.Add((path, v));
                        break;
                }
            }
            Walk(Current(name), "");
            return result;
        }

        private SourceState StateOf(string name)
        {
            if (!state.TryGetValue(name, out var st))
            {
                st = new SourceState();
                state[name] = st;
            }
            return st;
        }

        private JsonNode? Current(string name) => state.TryGetValue(name, out var st) ? st.Latest : null;

        // ---------------- live sources ----------------

        private async Task EnsureLiveAsync(string name)
        {
            var st = StateOf(name);
            if (st.LiveRunning || !sources.TryGetValue(name, out var source)) return;
            st.LiveRunning = true;
            var token = liveCancel.Token;
            if (source.Kind == FmiDataSourceKind.WebSocket)
            {
                _ = ReceiveLoopAsync(source, st, token);
                return;
            }
            await FetchAsync(name); // first value now, then refresh in the background
            _ = PollLoopAsync(source, token);
        }

        private async Task PollLoopAsync(FmiDataSource source, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(source.RefreshSeconds), token);
                    await FetchAsync(source.Name);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ReceiveLoopAsync(FmiDataSource source, SourceState st, CancellationToken token)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(source.Address), token);
                st.Error = "";
                var buffer = new byte[16 * 1024];
                var message = new StringBuilder();
                while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage) continue;
                    st.Latest = Parse(message.ToString(), source.Format);
                    st.FetchedAt = DateTime.Now;
                    message.Clear();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                st.Error = $"WebSocket {source.Address}: {ex.Message}";
            }
            finally
            {
                st.LiveRunning = false;
            }
        }

        private void StopLive()
        {
            liveCancel.Cancel();
            liveCancel = new CancellationTokenSource();
            foreach (var st in state.Values) st.LiveRunning = false;
        }

        // ---------------- parsing ----------------

        /// <summary>JSON as-is; CSV becomes an array of row objects keyed by the header row
        /// (numbers converted), so "2.Temperature" reads row 2, column Temperature.</summary>
        public static JsonNode? Parse(string text, FmiDataFormat format)
        {
            if (format == FmiDataFormat.Json)
            {
                try { return JsonNode.Parse(text); }
                catch (System.Text.Json.JsonException) { return JsonValue.Create(text.Trim()); } // plain-text payload
            }
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim() != "").ToList();
            var rows = new JsonArray();
            if (lines.Count == 0) return rows;
            var header = SplitCsvLine(lines[0]);
            foreach (var line in lines.Skip(1))
            {
                var cells = SplitCsvLine(line);
                var row = new JsonObject();
                for (var i = 0; i < header.Count; i++)
                {
                    var cell = i < cells.Count ? cells[i] : "";
                    var key = header[i] == "" ? $"col{i}" : header[i];
                    if (row.ContainsKey(key)) continue;
                    row[key] = double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        ? JsonValue.Create(number)
                        : JsonValue.Create(cell);
                }
                rows.Add(row);
            }
            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    cells.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            cells.Add(current.ToString().Trim());
            return cells;
        }

        private static readonly Regex PathSegment = new(@"([^.\[\]]+)|\[(\d+)\]");

        /// <summary>Walk "current.temperature_2m", "hourly.time[3]" or "2.Temperature" into a JSON value.</summary>
        public static bool TryReadPath(JsonNode root, string path, out string value, out string error)
        {
            value = "";
            var node = root;
            var walked = "";
            foreach (Match m in PathSegment.Matches(path ?? ""))
            {
                var key = m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value;
                if (key == "") continue;
                JsonNode? next = null;
                if (node is JsonObject obj)
                {
                    next = obj.TryGetPropertyValue(key, out var child) ? child : null;
                }
                else if (node is JsonArray arr && int.TryParse(key, out var index))
                {
                    next = index >= 0 && index < arr.Count ? arr[index] : null;
                }
                walked = walked == "" ? key : $"{walked}.{key}";
                if (next is null)
                {
                    error = $"path '{path}' not found (stopped at '{walked}')";
                    return false;
                }
                node = next;
            }
            if (node is JsonObject or JsonArray)
            {
                error = $"path '{path}' points to a {(node is JsonObject ? "group of fields" : "list")}, not a single value; add a field name or [index]";
                return false;
            }
            var json = node!.AsValue();
            if (json.TryGetValue<double>(out var d)) value = d.ToString(CultureInfo.InvariantCulture);
            else if (json.TryGetValue<bool>(out var b)) value = b ? "1" : "0";
            else if (json.TryGetValue<string>(out var s)) value = s;
            else value = json.ToJsonString();
            error = "";
            return true;
        }
    }
}
