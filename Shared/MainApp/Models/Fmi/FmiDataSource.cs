using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;

namespace FRAMLab.Shared
{
    /// <summary>How a live data source is reached from the browser.</summary>
    public enum FmiDataSourceKind
    {
        /// <summary>HTTP GET returning JSON or CSV: a URL, a REST API, or a REST layer in front of a database.</summary>
        Http,
        /// <summary>A WebSocket that pushes JSON messages (e.g. a sensor feed); the latest message is the value.</summary>
        WebSocket,
    }

    public enum FmiDataFormat { Json, Csv }

    /// <summary>When a function reads a data source, relative to its own evaluation.</summary>
    public enum FmiDataTiming
    {
        /// <summary>Fetched when the function is evaluated, before its equations; all reads in that evaluation see the same data.</summary>
        OnActivation,
        /// <summary>No fetch by the function: each read takes the source's latest value (WebSocket messages as they
        /// arrive, HTTP sources refreshed in the background every few seconds).</summary>
        Live,
        /// <summary>Fetched after the function has activated, before its outputs flow on; the next evaluation reads that data.</summary>
        OnExit,
    }

    /// <summary>
    /// A live data source declared in &lt;FMI&gt;&lt;DataSources&gt;, read in equations and scripts with
    /// api("name", "path"). Only the definition is stored in the .xfmv; secrets (API keys) stay in
    /// the user's browser and are sent in <see cref="AuthHeader"/> when one is named.
    /// </summary>
    public sealed class FmiDataSource
    {
        public static readonly Regex NamePattern = new("^[A-Za-z][A-Za-z0-9_]*$");

        public string Name { get; set; } = "";
        public FmiDataSourceKind Kind { get; set; } = FmiDataSourceKind.Http;
        public string Address { get; set; } = "";
        public FmiDataFormat Format { get; set; } = FmiDataFormat.Json;
        /// <summary>Background refresh for <see cref="FmiDataTiming.Live"/> reads of HTTP sources.</summary>
        public int RefreshSeconds { get; set; } = 10;
        /// <summary>Header that carries the browser-stored key, e.g. "Authorization" or "apikey"; "" = no key.</summary>
        public string AuthHeader { get; set; } = "";
        public string Description { get; set; } = "";

        public static FmiDataSource? FromXml(XmlElement el, List<FmiDiagnostic> diagnostics)
        {
            var name = el.GetAttribute("name");
            var source = $"FMI/DataSources/Source[{(name == "" ? "?" : name)}]";
            if (!NamePattern.IsMatch(name))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, "Data source name must match [A-Za-z][A-Za-z0-9_]*."));
                return null;
            }
            var kindRaw = el.GetAttribute("kind");
            var kind = FmiDataSourceKind.Http;
            if (kindRaw != "" && !Enum.TryParse(kindRaw, true, out kind))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, $"Unknown kind '{kindRaw}'. Expected http or websocket."));
                return null;
            }
            var address = el.GetAttribute("address").Trim();
            var scheme = kind == FmiDataSourceKind.WebSocket ? new[] { "ws://", "wss://" } : new[] { "http://", "https://" };
            if (!scheme.Any(s => address.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source,
                    $"Address must start with {string.Join(" or ", scheme)}."));
                return null;
            }
            var formatRaw = el.GetAttribute("format");
            var format = FmiDataFormat.Json;
            if (formatRaw != "" && !Enum.TryParse(formatRaw, true, out format))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, $"Unknown format '{formatRaw}'. Expected json or csv."));
                return null;
            }
            var refresh = 10;
            var refreshRaw = el.GetAttribute("refresh");
            if (refreshRaw != "" && (!int.TryParse(refreshRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out refresh) || refresh < 1))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, source, $"refresh must be a whole number of seconds ≥ 1; using 10."));
                refresh = 10;
            }
            return new FmiDataSource
            {
                Name = name,
                Kind = kind,
                Address = address,
                Format = format,
                RefreshSeconds = refresh,
                AuthHeader = el.GetAttribute("authHeader"),
                Description = el.GetAttribute("description"),
            };
        }

        public XmlElement ToXml(XmlDocument doc)
        {
            var el = doc.CreateElement("Source");
            el.SetAttribute("name", Name);
            el.SetAttribute("kind", Kind.ToString().ToLowerInvariant());
            el.SetAttribute("address", Address);
            if (Format != FmiDataFormat.Json) el.SetAttribute("format", Format.ToString().ToLowerInvariant());
            if (RefreshSeconds != 10) el.SetAttribute("refresh", RefreshSeconds.ToString(CultureInfo.InvariantCulture));
            if (AuthHeader != "") el.SetAttribute("authHeader", AuthHeader);
            if (Description != "") el.SetAttribute("description", Description);
            return el;
        }
    }

    /// <summary>A function's choice of when it reads a data source (&lt;FMI&gt;&lt;DataUses&gt;&lt;Use&gt;).
    /// Functions that call api() without a Use read on activation.</summary>
    public sealed class FmiDataUse
    {
        public string Source { get; set; } = "";
        public string FunctionId { get; set; } = "";
        public FmiDataTiming Timing { get; set; } = FmiDataTiming.OnActivation;

        public static FmiDataUse? FromXml(XmlElement el, List<FmiDiagnostic> diagnostics)
        {
            var sourceName = el.GetAttribute("source");
            var functionId = el.GetAttribute("function");
            var where = $"FMI/DataUses/Use[{sourceName}/{functionId}]";
            if (sourceName == "" || functionId == "")
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, where, "A data use needs both source and function."));
                return null;
            }
            var timing = FmiDataTiming.OnActivation;
            var timingRaw = el.GetAttribute("timing");
            if (timingRaw != "" && !Enum.TryParse(timingRaw, true, out timing))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, where,
                    $"Unknown timing '{timingRaw}'. Expected onActivation, live or onExit; using onActivation."));
                timing = FmiDataTiming.OnActivation;
            }
            return new FmiDataUse { Source = sourceName, FunctionId = functionId, Timing = timing };
        }

        public XmlElement ToXml(XmlDocument doc)
        {
            var el = doc.CreateElement("Use");
            el.SetAttribute("source", Source);
            el.SetAttribute("function", FunctionId);
            var timing = Timing.ToString();
            el.SetAttribute("timing", char.ToLowerInvariant(timing[0]) + timing[1..]);
            return el;
        }
    }
}
