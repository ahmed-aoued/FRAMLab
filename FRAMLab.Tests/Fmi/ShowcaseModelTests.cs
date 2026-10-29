using System.Net;
using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// TestFiles/FRAMLab_Showcase_Winter_Gritting.xfmv must stay a working demonstration of every
/// feature: it loads cleanly, every script compiles, and a full run (with fake weather) reaches
/// every function and both exit functions.
/// </summary>
public class ShowcaseModelTests
{
    private static XmlDocument Load()
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", "FRAMLab_Showcase_Winter_Gritting.xfmv"));
        return doc;
    }

    /// <summary>Answers the two Open-Meteo addresses of the model with a cold night.</summary>
    private sealed class FakeOpenMeteo : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.Query.Contains("format=csv")
                ? "latitude,longitude,elevation\n48.54,9.67,802.0\n\ntime,temperature_2m (°C),precipitation_probability (%)\n" +
                  string.Join("\n", Enumerable.Range(0, 24).Select(h => FormattableString.Invariant($"2026-12-01T{h:00}:00,{-1 - h * 0.1:0.0},{h * 3}")))
                : "{\"current\":{\"temperature_2m\":1.2,\"relative_humidity_2m\":93,\"dew_point_2m\":0.4,\"precipitation\":0.3,\"wind_speed_10m\":18.5}," +
                  "\"daily\":{\"temperature_2m_min\":[-2.1]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void Definitions_LoadWithoutProblems()
    {
        var definition = new FmiDefinitionService().Load(Load());

        Assert.Empty(definition.LoadDiagnostics);
        Assert.Equal(3, definition.Variables.Count);
        Assert.Equal(new[] { "clamp01", "iceRisk", "saltTonnes" }, definition.Macros.Select(m => m.Name));
        Assert.Equal(new[] { "weather", "forecast" }, definition.DataSources.Select(s => s.Name));
        Assert.Equal(FmiDataTiming.Live, definition.TimingFor("weather", "1"));
        Assert.Equal(FmiDataTiming.OnExit, definition.TimingFor("weather", "9"));
    }

    [Fact]
    public void EveryScript_Compiles()
    {
        var host = new FmiRunEngineTests.TestRunHost(Load());

        foreach (var fn in host.Functions.Where(f => !string.IsNullOrWhiteSpace(f.Script.Source)))
        {
            var compiled = host.Scripting.Compile(fn);
            Assert.False(compiled.HasErrors, $"{fn.IDName}: " + string.Join("; ", compiled.Diagnostics.Select(d => $"{d.StartLine}:{d.StartColumn} {d.Message}")));
        }
    }

    [Fact]
    public async Task FullRun_ReachesEveryFunction()
    {
        var host = new FmiRunEngineTests.TestRunHost(Load());
        host.UseData(new FmiDataService(new HttpClient(new FakeOpenMeteo()), _ => Task.FromResult<string?>(null)));
        var engine = new FmiRunEngine(host);

        var results = await host.RunUntilDoneAsync(engine, 20);

        Assert.Equal(FmiRunEngine.ResultEnd, results.Last());
        Assert.Empty(engine.UnknownVariables);
        Assert.Empty(host.Errors);
        Assert.All(host.Functions, fn => Assert.True(fn.wasActive, $"{fn.IDName} never activated"));
        string Value(string fnId, string key) => host.Fn(fnId).metadata.Single(m => m[0] == key)[1];

        Assert.Equal("1.2", Value("0", "Air_temp"));                  // api() on activation
        Assert.Equal("-1.3", Value("1", "Surface_min"));              // sensor script, live data, 2-D array
        Assert.True(double.Parse(Value("2", "Ice_risk"), System.Globalization.CultureInfo.InvariantCulture) > 0.5); // macros
        Assert.Equal("3", Value("5", "Risk_level"));                  // CASE in the decision script
        Assert.Equal("Gritting order, Ice warning", Value("5", "Activate"));
        Assert.Equal("1", host.Fn("5").FunctionType);                 // Variable key
        Assert.Equal("4", Value("6", "Trucks"));
        Assert.Equal("2", Value("6", "Salt_loaded"));                 // function-scoped macro: 100 km * 0.02 t
        Assert.Equal("-1.6", Value("7", "Rush_hour_temp"));           // CSV data source, row 6
        Assert.Equal("48", Value("9", "Roads_treated"));              // 4 routes * 12 km
        Assert.Equal("48", Value("11", "Km_total"));                  // aggregate Isum_
        Assert.Equal("Black ice on the A8 climb", Value("12", "Message")); // nested ?:
        Assert.Contains(host.Trace, t => t.Contains("decision: grit all routes"));
        Assert.Contains(host.Trace, t => t.Contains("effective for about 9 h"));
        host.Data!.Reset();
    }
}

/// <summary>Models with numeric Global_* keys must survive the first cycle (regression).</summary>
public class GlobalKeyTests
{
    [Fact]
    public async Task CityTrip_WithNumericGlobals_Runs()
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", "City_trip_new_samplesolution.xfmv"));
        var host = new FmiRunEngineTests.TestRunHost(doc);

        var result = await new FmiRunEngine(host).RunCycleAsync();

        Assert.NotEqual(FmiRunEngine.ResultError, result);
    }
}

/// <summary>Numbers must reach equations with a decimal point in every culture (German browsers use ",").</summary>
public class CultureTests
{
    [Fact]
    public void ComputedDecimals_ChainCorrectly_InAGermanCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            const string fnXml = "<Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>F1</IDName>" +
                                 "<metadata><A active=\"true\" equation=\"0.5 - 7.8\">0</A><B active=\"true\" equation=\"A * 2\">0</B></metadata></Function>";
            var (interpreter, _, functions) = InterpreterFixture.FromXml($"<FM><Functions>{fnXml}</Functions></FM>");

            InterpreterFixture.RunCycle(interpreter, functions, 0);
            InterpreterFixture.RunCycle(interpreter, functions, 1); // A is now a typed number variable

            Assert.Equal("-14.6", functions[0].metadata.Single(m => m[0] == "B")[1]);
            Assert.Equal("-7.3", functions[0].getVariables()["A"][0]);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
