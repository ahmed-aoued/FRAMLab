using System.Text;
using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// The FMI run loop (FmiRunEngine) driven end to end without the UI: model check, function
/// classification, activation order through couplings, Entry timing, unknown-variable blocking.
/// </summary>
public class FmiRunEngineTests
{
    private static XmlDocument LoadModel(string fileName)
    {
        var doc = new XmlDocument();
        doc.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", fileName));
        return doc;
    }

    [Fact]
    public async Task ScriptingExample_RunsFromEntryToExit_InCouplingOrder()
    {
        var host = new TestRunHost(LoadModel("FMV_Scripting_Example.xfmv"));
        var engine = new FmiRunEngine(host);

        var results = await host.RunUntilDoneAsync(engine, maxCycles: 10);

        Assert.Equal(new[] { "0", "1", "2", FmiRunEngine.ResultEnd }, results);
        Assert.Equal(new[] { "Detect fire" }, host.ActivatedIn(0));
        Assert.Equal(new[] { "Assess severity" }, host.ActivatedIn(1));
        Assert.Equal(new[] { "Alarm occupants", "Dispatch crew" }, host.ActivatedIn(2));
        Assert.Equal(new[] { "Report outcome" }, host.ActivatedIn(3));
        Assert.Equal("Entry", host.Fn("0").fmiType);
        Assert.Equal("Passthrough", host.Fn("1").fmiType);
        Assert.Equal("Exit", host.Fn("4").fmiType);
        Assert.Empty(host.Errors);
    }

    [Fact]
    public async Task ModelCheck_FailsOnOrphans_AndShowsTheLog()
    {
        var host = new TestRunHost(LoadModel("FMV_FeatureTest.xfmv")); // fn 1, 3, 4 have orphans
        var engine = new FmiRunEngine(host);

        Assert.Equal(FmiRunEngine.ResultError, await engine.RunCycleAsync());
        Assert.True(host.LogShown);
        Assert.Contains(host.Entries, e => e.Level == FmiLogLevel.Warning && e.Text.Contains("<Assess severity> has an orphan"));
    }

    [Fact]
    public async Task UnknownVariable_BlocksTheRun_UntilTheUserDecides()
    {
        var doc = LoadModel("FMV_Scripting_Example.xfmv");
        // Assess severity gets an equation using a variable nothing defines
        var md = doc.SelectSingleNode("//FM/Functions/Function[IDNr=1]/metadata")!;
        var key = doc.CreateElement("Escalation");
        key.SetAttribute("active", "true");
        key.SetAttribute("equation", "Severity_level * 2");
        key.InnerText = "5";
        md.AppendChild(key);
        var host = new TestRunHost(doc);
        var engine = new FmiRunEngine(host);

        Assert.Equal("0", await engine.RunCycleAsync());
        engine.PropagateCouplings();
        Assert.Equal(FmiRunEngine.ResultBlocked, await engine.RunCycleAsync()); // cycle 1 evaluates Assess severity

        var unknown = Assert.Single(engine.UnknownVariables);
        Assert.Equal(("1", "Escalation", "Severity_level"), (unknown.FunctionId, unknown.Key, unknown.Variable));
        Assert.Equal("5", host.Fn("1").metadata.Single(m => m[0] == "Escalation")[1]); // not computed from a made-up 0

        // the user chooses "Read as 0": the run continues and the key is computed
        host.Interpreter.AcceptedUnknownVariables.Add(unknown.AcceptKey);
        engine.PropagateCouplings();
        Assert.NotEqual(FmiRunEngine.ResultBlocked, await engine.RunCycleAsync());
        Assert.Empty(engine.UnknownVariables);
    }

    [Fact]
    public async Task EntryFunction_FiresEveryNCycles()
    {
        var doc = LoadModel("FMV_Scripting_Example.xfmv");
        doc.SelectSingleNode("//FM/Functions/Function[IDNr=0]/@profileFn")!.Value = "2";
        var host = new TestRunHost(doc);
        var engine = new FmiRunEngine(host) { StepTotal = 4 };

        await host.RunUntilDoneAsync(engine, maxCycles: 10);

        var entryCycles = host.Entries.Where(e => e.Text.StartsWith("Entry Function <Detect fire> has been activated")).Select(e => e.Cycle);
        Assert.Equal(new[] { 0, 2, 4 }, entryCycles);
    }

    [Fact]
    public void Usage_ReportsScriptsAndMacroCalls()
    {
        var doc = LoadModel("FMV_Scripting_Example.xfmv");
        var functions = doc.SelectNodes("//FM/Functions/Function")!.Cast<XmlNode>().Select(n => new Function(n)).ToList();
        functions[1].metadata.Add(new[] { "Risk", "", "true", "clamp01(Severity_assessment) + 1", "" });
        var definition = new FmiModelDefinition();
        definition.Macros.Add(new FmiMacroDefinition { Name = "clamp01", Body = "x" });

        var usage = FmiModelUsage.Analyze(functions, definition, scriptGlobalCount: 0);

        Assert.Equal(5, usage.ScriptFunctions.Count);
        Assert.True(usage.CallsMacros("1"));
        Assert.Equal(new[] { "clamp01" }, usage.MacroCalls["1"]["Risk"]);
        Assert.False(usage.CallsMacros("0"));
        Assert.Contains("Risk ← clamp01", usage.Describe("1"));
    }

    /// <summary>A UI-free host: interpreter + scripting engine over a parsed .xfmv.</summary>
    internal sealed class TestRunHost : IFmiRunHost
    {
        private readonly MetadataService metadata = new();
        private readonly ScriptingService scripting = new(NullLogger<ScriptingService>.Instance);

        public TestRunHost(XmlDocument doc)
        {
            foreach (XmlNode fnNode in doc.SelectNodes("//FM/Functions/Function")!)
            {
                if (fnNode.SelectSingleNode("metadata") is not null) metadata.CreateMetadataTable(fnNode);
                var fn = new Function(fnNode);
                fn.orphans = int.TryParse(fnNode.SelectSingleNode("@orphans")?.Value, out var o) ? o : 0;
                Functions.Add(fn);
            }
            foreach (var (section, type) in new[] { ("Inputs", "Input"), ("Preconditions", "Precondition"), ("Resources", "Resource"), ("Controls", "Control"), ("Times", "Time"), ("Outputs", "Output") })
            {
                foreach (XmlNode a in doc.SelectNodes($"//FM/{section}/{type}")!)
                {
                    var aspect = new Aspect(type, a.SelectSingleNode("IDNr")?.InnerText ?? "", "false",
                        a.SelectSingleNode("IDName")?.InnerText ?? "", a.SelectSingleNode("FunctionIDNr")?.InnerText ?? "");
                    (type == "Output" ? Outputs : Aspects).Add(aspect);
                }
            }
            // Couplings are derived like the app does on load: an Output and an I/P/R/C/T aspect
            // of another function with exactly the same name.
            Couplings = new List<Coupling>();
            foreach (var output in Outputs)
            {
                foreach (var target in Aspects.Where(a => a.IDName == output.IDName && a.FunctionIDNr != output.FunctionIDNr))
                {
                    var toType = target.Type.Substring(0, 1);
                    Couplings.Add(new Coupling($"{output.FunctionIDNr}|{output.IDName}|{target.FunctionIDNr}|{toType}", 0, 0, "from", "to", "",
                        output.FunctionIDNr, output.IDName, target.FunctionIDNr, toType, 0, 0, 0, 0));
                }
            }
            foreach (var fn in Functions)
            {
                fn.Script.SyncAspects(Couplings, fn.IDNr, AspectVariable.DeclaredFromXml(doc, fn.IDNr));
            }
            var defService = new FmiDefinitionService();
            Interpreter = new FmiInterpreter(new ExpressionEvaluator(), metadata);
            Interpreter.Initialize(defService.MergeWithImplicit(defService.Load(doc), Functions), Functions);
        }

        /// <summary>Connect live data (as Program.cs does in the app).</summary>
        public void UseData(FmiDataService data)
        {
            Data = data;
            Interpreter.Data = data;
            scripting.Data = data;
            data.Configure(Interpreter.Definition.DataSources, Interpreter.Definition.DataUses);
        }

        public FmiInterpreter Interpreter { get; }
        public List<Function> Functions { get; } = new();
        public List<Coupling> Couplings { get; }
        public List<Aspect> Aspects { get; } = new();
        public List<Aspect> Outputs { get; } = new();
        public int Cycle { get; set; } = -1;
        public bool LogShown { get; private set; }
        public List<(int Cycle, FmiLogLevel Level, string Text)> Entries { get; } = new();
        public List<string> Errors { get; } = new();

        private FmiEvaluationContext Ctx => new() { FunctionList = Functions, CouplingList = Couplings, Cycle = Cycle };

        public void BeginCycle() => Interpreter.BeginCycle(Ctx);
        public void EndCycle() => Interpreter.EndCycle(Ctx);

        public FmiDataService? Data { get; set; }

        public async Task AfterActivateAsync(Function fn)
        {
            if (Data is not null) await Data.AfterActivateAsync(fn);
        }

        public async Task<IReadOnlyList<FmiUnknownVariable>> EvaluateAsync(Function fn)
        {
            if (Data is not null) await Data.BeforeEvaluateAsync(fn);
            var result = Interpreter.EvaluateFunction(fn, Ctx);
            var script = scripting.ExecuteCycle(fn, Cycle);
            if (script is not null)
            {
                Errors.AddRange(script.RuntimeIssues.Where(i => i.Severity == "Error").Select(i => $"{fn.IDName}: {i.Message}"));
            }
            return result.UnknownVariables;
        }

        public void SetFunctionType(Function fn, string functionType) => fn.FunctionType = functionType;
        public void StartLog() => Entries.Clear();
        public void Log(string text, Function? fn = null, FmiLogLevel level = FmiLogLevel.Info)
        {
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Entries.Add((Cycle, level, line));
            }
        }
        public void ShowLog() => LogShown = true;

        public Function Fn(string id) => Functions.Single(f => f.IDNr == id);

        public IEnumerable<string> ActivatedIn(int cycle) => Entries
            .Where(e => e.Cycle == cycle && e.Text.EndsWith("has been activated") && !e.Text.StartsWith("Background"))
            .Select(e => e.Text[(e.Text.IndexOf('<') + 1)..e.Text.IndexOf('>')])
            .Distinct();

        /// <summary>Drive the engine like FMIControl's Play button: cycle, then propagate couplings.</summary>
        public async Task<List<string>> RunUntilDoneAsync(FmiRunEngine engine, int maxCycles)
        {
            var results = new List<string>();
            for (var i = 0; i < maxCycles; i++)
            {
                var result = await engine.RunCycleAsync();
                engine.PropagateCouplings();
                results.Add(result);
                if (result is FmiRunEngine.ResultEnd or FmiRunEngine.ResultError or FmiRunEngine.ResultBlocked) break;
            }
            return results;
        }
    }
}
