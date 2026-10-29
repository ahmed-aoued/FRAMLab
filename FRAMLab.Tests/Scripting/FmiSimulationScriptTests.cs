using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace FRAMLab.Tests.Scripting;

/// <summary>
/// End-to-end simulation of <c>TestFiles/FMV_Scripting_Full_Feature.xfmv</c>.
/// Mirrors the FMI cycle pipeline: entry function first, background functions,
/// then the downstream functions that became active through couplings. Trace
/// messages from <c>Print(...)</c> are collected exactly as FMIControl appends
/// them to the FMI log.
/// </summary>
public class FmiSimulationScriptTests
{
    private readonly ITestOutputHelper output;

    public FmiSimulationScriptTests(ITestOutputHelper output) => this.output = output;

    private static string? Var(Function fn, string key)
    {
        var vars = fn.getVariables();
        return vars.TryGetValue(key, out var value) && value.Length > 0 ? value[0] : null;
    }

    [Fact]
    public void FullScriptingFeature_RunsFmiSimulation_AndWritesToLog()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_Scripting_Full_Feature.xfmv");
        Assert.True(File.Exists(modelPath), $"Example model not copied to test output: {modelPath}");

        var doc = new XmlDocument();
        doc.Load(modelPath);

        var functions = doc.SelectNodes("//FM/Functions/Function")!
            .Cast<XmlNode>()
            .Select(n => new Function(n))
            .ToList();
        var byId = functions.ToDictionary(f => f.IDNr);
        var couplings = AspectVariable.CouplingsFromXml(doc);
        foreach (var fn in functions)
        {
            fn.Script.SyncAspects(couplings, fn.IDNr, AspectVariable.DeclaredFromXml(doc, fn.IDNr));
        }

        Assert.Equal(3, functions.Count);
        Assert.Equal(2, couplings.Count);

        var entry = byId["0"];   // Sensor array  (Entry)
        var filter = byId["1"];  // Filter        (Passthrough)
        var exit = byId["2"];    // Statistics    (Exit)
        var entryCoupling = couplings.Single(c => c.outputFn == "0" && c.toFn == "1");
        var filterCoupling = couplings.Single(c => c.outputFn == "1" && c.toFn == "2");

        var svc = new ScriptingService(NullLogger<ScriptingService>.Instance);
        svc.Reset();
        var fmiLog = new List<string>();

        ScriptExecutionContext Evaluate(Function fn, int cycle)
        {
            var ctx = svc.ExecuteCycle(fn, cycle);
            Assert.NotNull(ctx);
            Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
            fmiLog.AddRange(ctx.Trace); // FMIControl.ExecuteFunctionScript does the same
            return ctx;
        }

        // bFMI_cycle_Couplings: copy the producer metadata into the consumer's
        // "{toType}{outputFn}_{key}" variables at the end of a cycle.
        void Propagate(Function producer, Coupling coupling)
        {
            var consumer = byId[coupling.toFn];
            foreach (var meta in producer.metadata)
            {
                consumer.newVariable($"{coupling.toType}{producer.IDNr}_{meta[0]}", new[] { meta[1], meta[1] });
            }
        }

        const int cycles = 5;
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            // The entry function (and its global audit array) runs every cycle.
            Evaluate(entry, cycle);

            // Couplings applied at the end of cycle 0/1 activate downstream
            // functions in the following cycle, as in FMIControl.
            if (cycle >= 1) Evaluate(filter, cycle);
            if (cycle >= 2) Evaluate(exit, cycle);

            Propagate(entry, entryCoupling);
            if (cycle >= 1) Propagate(filter, filterCoupling);
        }

        output.WriteLine("================ SIMULATED FMI LOG ================");
        foreach (var line in fmiLog) output.WriteLine(line);
        output.WriteLine("===================================================");

        // --- entry: one log line per cycle, array window evolves ---
        Assert.Equal(cycles, fmiLog.Count(l => l.Contains("Sensor window=")));
        Assert.Contains(fmiLog, l => l.Contains("Sensor window=[0,0,0,0,0]") && l.Contains("average=0"));
        Assert.Contains(fmiLog, l => l.Contains("Sensor window=[0,1,2,3,4]") && l.Contains("average=2"));
        Assert.Contains(fmiLog, l => l.Contains("[fn 0 cycle 4]"));

        // --- global audit array: logged every cycle ---
        Assert.Equal(cycles, fmiLog.Count(l => l.Contains("Audit array=")));
        Assert.Contains(fmiLog, l => l.Contains("Audit array=[0,1,2,3]"));
        Assert.Contains(fmiLog, l => l.Contains("Audit array=[4,1,2,3]"));

        // --- filter: runs once the coupling is active, arrays + branch logic ---
        Assert.Equal(cycles - 1, fmiLog.Count(l => l.Contains("Filter history=")));
        Assert.Contains(fmiLog, l => l.Contains("Filter history=[0,0,0]") && l.Contains("gain=0.9"));

        // --- exit: runs behind the filter, static report counter grows ---
        Assert.Equal(cycles - 2, fmiLog.Count(l => l.Contains("Exit reports=")));
        Assert.Contains(fmiLog, l => l.Contains("Exit reports=1"));
        Assert.Contains(fmiLog, l => l.Contains("Exit reports=3"));

        // --- scripted outputs reach the FMI variables used by the couplings ---
        Assert.Equal("2", Var(entry, "Raw_signal"));
        Assert.NotNull(Var(filter, "Filtered_signal"));
    }
}
