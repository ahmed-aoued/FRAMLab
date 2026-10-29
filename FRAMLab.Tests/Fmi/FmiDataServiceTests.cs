using System.Net;
using System.Text.Json.Nodes;
using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Live data for api("source", "path"): path reading (JSON/CSV), keys from the browser, the three
/// per-function timings, and api() in equations, macros and scripts, against a fake HTTP server.
/// </summary>
public class FmiDataServiceTests
{
    /// <summary>A fake server: every request to /weather returns a temperature one higher than the last.</summary>
    private sealed class FakeWeather : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Calls++;
            var body = $"{{\"current\":{{\"temperature_2m\":{Calls}.5,\"relative_humidity_2m\":80,\"wind_speed_10m\":12.4,\"is_day\":true,\"summary\":\"cloudy\"}},\"hourly\":{{\"temperature_2m\":[7,8,9]}}}}";
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(body) });
        }
    }

    private static FmiDataSource Weather(string authHeader = "") => new()
    {
        Name = "weather", Address = "https://example.test/weather", AuthHeader = authHeader,
    };

    private static Function FunctionReading(string equation, string id = "1")
    {
        var doc = new XmlDocument();
        doc.LoadXml($"<Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>{id}</IDNr><FunctionType>0</FunctionType><IDName>F{id}</IDName>" +
                    $"<metadata><Temp active=\"true\" equation=\"{equation}\">0</Temp></metadata></Function>");
        return new Function(doc.DocumentElement!);
    }

    private static (FmiDataService Data, FakeWeather Server) NewService(FmiDataTiming timing = FmiDataTiming.OnActivation, string? secret = null, string authHeader = "")
    {
        var server = new FakeWeather();
        var data = new FmiDataService(new HttpClient(server), _ => Task.FromResult(secret));
        data.Configure(new[] { Weather(authHeader) }, new[] { new FmiDataUse { Source = "weather", FunctionId = "1", Timing = timing } });
        return (data, server);
    }

    private static string Read(FmiDataService data, string path = "current.temperature_2m")
    {
        Assert.True(data.TryRead("1", "weather", path, out var value, out var error), error);
        return value;
    }

    [Theory]
    [InlineData("current.temperature_2m", "10.6")]
    [InlineData("current.is_day", "1")]
    [InlineData("current.summary", "cloudy")]
    [InlineData("hourly.temperature_2m[1]", "8")]
    [InlineData("hourly.temperature_2m.2", "9")]
    public void ReadsJsonPaths(string path, string expected)
    {
        var json = JsonNode.Parse("{\"current\":{\"temperature_2m\":10.6,\"is_day\":true,\"summary\":\"cloudy\"},\"hourly\":{\"temperature_2m\":[7,8,9]}}")!;

        Assert.True(FmiDataService.TryReadPath(json, path, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("current.wind", "not found (stopped at 'current.wind')")]
    [InlineData("current", "points to a group of fields")]
    [InlineData("hourly.temperature_2m", "points to a list")]
    public void ExplainsBadPaths(string path, string message)
    {
        var json = JsonNode.Parse("{\"current\":{\"temperature_2m\":10.6},\"hourly\":{\"temperature_2m\":[7,8,9]}}")!;

        Assert.False(FmiDataService.TryReadPath(json, path, out _, out var error));
        Assert.Contains(message, error);
    }

    [Fact]
    public void Csv_BecomesRows_KeyedByHeader()
    {
        var rows = FmiDataService.Parse("time,Temperature,Note\n2026-10-29T00:00,8.5,\"cold, wet\"\n2026-10-29T01:00,8.1,ok\n", FmiDataFormat.Csv)!;

        Assert.True(FmiDataService.TryReadPath(rows, "1.Temperature", out var temp, out _));
        Assert.Equal("8.1", temp);
        Assert.True(FmiDataService.TryReadPath(rows, "0.Note", out var note, out _));
        Assert.Equal("cold, wet", note);
    }

    [Fact]
    public void Csv_WithPreamble_UsesTheTable_AndDropsUnits()
    {
        // the shape of Open-Meteo's format=csv: a location block, an empty line, then the table
        const string csv = "latitude,longitude,elevation\n48.54,9.67,802.0\n\n" +
                           "time,temperature_2m (°C),precipitation_probability (%)\n" +
                           "2026-10-29T00:00,4.5,0\n2026-10-29T01:00,4.4,10\n2026-10-29T02:00,3.8,20\n";

        var rows = FmiDataService.Parse(csv, FmiDataFormat.Csv)!;

        Assert.True(FmiDataService.TryReadPath(rows, "2.temperature_2m", out var temp, out var error), error);
        Assert.Equal("3.8", temp);
        Assert.True(FmiDataService.TryReadPath(rows, "1.precipitation_probability", out var rain, out _));
        Assert.Equal("10", rain);
    }

    [Fact]
    public async Task SendsTheBrowserStoredKey_InTheNamedHeader()
    {
        var (data, server) = NewService(secret: "Bearer s3cret", authHeader: "Authorization");

        await data.FetchAsync("weather");

        var request = Assert.Single(server.Requests);
        Assert.Equal("Bearer s3cret", request.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task OnActivation_FetchesBeforeEveryEvaluation()
    {
        var (data, server) = NewService(FmiDataTiming.OnActivation);
        var fn = FunctionReading("api(&quot;weather&quot;, &quot;current.temperature_2m&quot;)");

        await data.BeforeEvaluateAsync(fn);
        Assert.Equal("1.5", Read(data));
        await data.AfterActivateAsync(fn); // no effect for this timing
        await data.BeforeEvaluateAsync(fn);
        Assert.Equal("2.5", Read(data));
        Assert.Equal(2, server.Calls);
    }

    [Fact]
    public async Task OnExit_FetchesAfterActivation_ForTheNextEvaluation()
    {
        var (data, server) = NewService(FmiDataTiming.OnExit);
        var fn = FunctionReading("api(&quot;weather&quot;, &quot;current.temperature_2m&quot;)");

        await data.BeforeEvaluateAsync(fn); // first time: nothing captured at an exit yet
        Assert.Equal("1.5", Read(data));
        await data.AfterActivateAsync(fn);  // captured when the function finished
        await data.BeforeEvaluateAsync(fn); // next evaluation: no new fetch
        Assert.Equal("2.5", Read(data));
        Assert.Equal(2, server.Calls);
    }

    [Fact]
    public async Task Live_ReadsTheLatestValue_WithoutFetchingPerEvaluation()
    {
        var (data, server) = NewService(FmiDataTiming.Live);
        var fn = FunctionReading("api(&quot;weather&quot;, &quot;current.temperature_2m&quot;)");

        await data.BeforeEvaluateAsync(fn); // starts the background refresh, first value now
        Assert.Equal("1.5", Read(data));
        await data.BeforeEvaluateAsync(fn); // already live: no extra fetch
        Assert.Equal(1, server.Calls);
        await data.FetchAsync("weather");   // what the background refresh does
        Assert.Equal("2.5", Read(data));
        data.Reset();
    }

    [Fact]
    public void Missing_Source_Or_Data_IsExplained()
    {
        var (data, _) = NewService();

        Assert.False(data.TryRead("1", "traffic", "x", out _, out var error));
        Assert.Contains("No data source named 'traffic'", error);
        Assert.False(data.TryRead("1", "weather", "current.temperature_2m", out _, out error));
        Assert.Contains("no data has arrived yet", error);
    }

    // ---------- in a run ----------

    private static XmlDocument ModelWithWeather(string equation, string extraFmi = "")
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_Scripting_Example.xfmv"));
        var md = doc.SelectSingleNode("//FM/Functions/Function[IDNr=1]/metadata")!;
        var key = doc.CreateElement("Outside_temp");
        key.SetAttribute("active", "true");
        key.SetAttribute("equation", equation);
        key.InnerText = "-99";
        md.AppendChild(key);
        var fmi = doc.CreateElement("FMI");
        fmi.SetAttribute("version", "1");
        fmi.InnerXml = "<Variables/><DataSources><Source name=\"weather\" kind=\"http\" address=\"https://example.test/weather\" /></DataSources>" + extraFmi;
        doc.DocumentElement!.AppendChild(fmi);
        return doc;
    }

    private static string Value(Function fn, string key) => fn.metadata.Single(m => m[0] == key)[1];

    [Fact]
    public async Task Equation_ReadsLiveData_DuringARun()
    {
        var host = new FmiRunEngineTests.TestRunHost(ModelWithWeather("round(api(\"weather\", \"current.temperature_2m\") * 2, 0)"));
        var server = new FakeWeather();
        host.UseData(new FmiDataService(new HttpClient(server), _ => Task.FromResult<string?>(null)));
        var engine = new FmiRunEngine(host);

        var results = await host.RunUntilDoneAsync(engine, 10);

        Assert.Equal(FmiRunEngine.ResultEnd, results.Last());
        Assert.Equal("3", Value(host.Fn("1"), "Outside_temp")); // fetched once, when Assess severity activated: 1.5 * 2
        Assert.Equal(1, server.Calls);
    }

    [Fact]
    public async Task Macro_CanWrapApi()
    {
        var host = new FmiRunEngineTests.TestRunHost(ModelWithWeather("toFahrenheit(api(\"weather\", \"current.temperature_2m\"))",
            "<Macros><Macro name=\"toFahrenheit\"><Param name=\"c\" type=\"number\" /><Body>c * 9 / 5 + 32</Body></Macro></Macros>"));
        host.UseData(new FmiDataService(new HttpClient(new FakeWeather()), _ => Task.FromResult<string?>(null)));

        await host.RunUntilDoneAsync(new FmiRunEngine(host), 10);

        Assert.Equal("34.7", Value(host.Fn("1"), "Outside_temp"));
    }

    [Fact]
    public async Task FailedRead_PausesTheRun_WithTheReason()
    {
        var host = new FmiRunEngineTests.TestRunHost(ModelWithWeather("api(\"weather\", \"current.temperature_2m\")"));
        host.UseData(new FmiDataService(new HttpClient(new FakeWeather { Status = HttpStatusCode.NotFound }), _ => Task.FromResult<string?>(null)));
        var engine = new FmiRunEngine(host);

        var results = await host.RunUntilDoneAsync(engine, 10);

        Assert.Equal(FmiRunEngine.ResultBlocked, results.Last());
        var unknown = Assert.Single(engine.UnknownVariables);
        Assert.Equal("api(\"weather\", \"current.temperature_2m\")", unknown.Variable);
        Assert.Contains("HTTP 404", unknown.Reason);
        Assert.Equal("-99", Value(host.Fn("1"), "Outside_temp")); // kept, not replaced by a made-up 0
    }

    [Fact]
    public async Task Script_ReadsLiveData_WithAPI()
    {
        var (data, _) = NewService();
        var fn = FunctionReading("1");
        fn.Script.Source = "Print('outside ', API('weather', 'current.summary'), ' ', API('weather', 'current.temperature_2m') + 1);";
        await data.FetchAsync("weather");
        var scripting = new ScriptingService(NullLogger<ScriptingService>.Instance) { Data = data };

        var ctx = scripting.ExecuteCycle(fn, 1);

        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.EndsWith("outside cloudy 2.5", Assert.Single(ctx.Trace));
    }

    [Fact]
    public async Task WeatherShowcaseModel_RunsWithAllThreeTimings()
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_LiveData_Weather.xfmv"));
        var host = new FmiRunEngineTests.TestRunHost(doc);
        var server = new FakeWeather();
        host.UseData(new FmiDataService(new HttpClient(server), _ => Task.FromResult<string?>(null)));
        var engine = new FmiRunEngine(host);

        var results = await host.RunUntilDoneAsync(engine, 10);

        Assert.Equal(FmiRunEngine.ResultEnd, results.Last());
        Assert.Empty(host.Errors);
        Assert.Equal("1.5", Value(host.Fn("1"), "Outside_temp"));   // on activation
        Assert.Equal("80", Value(host.Fn("1"), "Humidity"));
        Assert.Equal("36.5", Value(host.Fn("2"), "Outside_temp_F")); // macro over api(), on exit: 2.5 °C fetched when first evaluated
        Assert.Equal(FmiDataTiming.Live, host.Interpreter.Definition.TimingFor("weather", "3"));
        host.Data!.Reset();
    }

    [Fact]
    public void DataSources_AndTimings_RoundTripThroughTheFile()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Functions/></FM>");
        var service = new FmiDefinitionService();
        service.UpsertDataSource(doc, new FmiDataSource { Name = "sensors", Kind = FmiDataSourceKind.WebSocket, Address = "wss://example.test/feed", AuthHeader = "apikey" });
        service.SetDataTiming(doc, "sensors", "3", FmiDataTiming.Live);

        var definition = service.Load(doc);

        var source = Assert.Single(definition.DataSources);
        Assert.Equal((FmiDataSourceKind.WebSocket, "wss://example.test/feed", "apikey"), (source.Kind, source.Address, source.AuthHeader));
        Assert.Equal(FmiDataTiming.Live, definition.TimingFor("sensors", "3"));
        Assert.Equal(FmiDataTiming.OnActivation, definition.TimingFor("sensors", "4"));
        Assert.DoesNotContain("s3cret", doc.OuterXml); // keys never go into the file
        Assert.True(service.RemoveDataSource(doc, "sensors"));
        Assert.Empty(service.Load(doc).DataUses); // timings of a removed source go with it
    }
}
