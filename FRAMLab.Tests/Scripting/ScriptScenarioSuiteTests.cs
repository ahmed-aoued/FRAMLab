using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>
/// Port of FRAMLAB/TestFiles/ScriptScenarioTests.cs. Drives the production
/// ScriptingService with realistic function XML and a full four-function model,
/// mimicking the FMI coupling propagation the app performs.
/// </summary>
public class ScriptScenarioSuiteTests
{
    private static string? Var(Function fn, string key)
    {
        var vars = fn.getVariables();
        return vars.TryGetValue(key, out var value) && value.Length > 0 ? value[0] : null;
    }

    private static Function MakeFn(string id, string name, string? script = null)
    {
        var d = new XmlDocument();
        d.LoadXml($"<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\">" +
                  $"<IDNr>{id}</IDNr><FunctionType>0</FunctionType><IDName>{name}</IDName></Function></Functions></FM>");
        var fn = new Function(d.SelectSingleNode("//Function")!);
        if (script is not null) fn.Script.Source = script;
        fn.Script.Enabled = true;
        return fn;
    }

    [Fact]
    public void ScenarioSuite()
    {
        var failures = new List<string>();
        void Check(string name, bool cond, string? detail = null)
        {
            if (!cond) failures.Add(detail is null ? name : $"{name} -> {detail}");
        }

        var scripting = new ScriptingService(NullLogger<ScriptingService>.Instance);

        // A. reading an Input aspect resolves the pipeline's variable name
        {
            var fn = MakeFn("1", "Assess severity", "result := Fire_signal;");
            fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Fire signal", Type = ScriptVarType.REAL });
            fn.Script.Variables.Add(new ScriptVariable { Name = "result", Scope = ScriptScope.Temp, Type = ScriptVarType.REAL, InitialValue = "0", IsOutput = true });
            fn.newVariable("I0_Fire_signal", new[] { "4.0", "propagated" });

            var ctx = scripting.ExecuteCycle(fn, 0);
            Check("Input aspect 'Fire signal' resolves I0_Fire_signal (=4)",
                Var(fn, "result") == "4", $"got '{Var(fn, "result")}'");
            Check("Input aspect read produces no runtime error",
                ctx is not null && !ctx.RuntimeIssues.Any(i => i.Severity == "Error"));
        }

        // B. writing an Output aspect reaches the coupling pipeline
        {
            var fn = MakeFn("0", "Detect fire", "Fire_signal := 1.0;");
            fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Output, Label = "Fire signal", Type = ScriptVarType.REAL });
            fn.metadata.Add(new[] { "Fire_signal", "0", "", "", "0" });

            scripting.ExecuteCycle(fn, 0);

            Check("Output aspect writes function variable 'Fire_signal'",
                Var(fn, "Fire_signal") == "1", $"got '{Var(fn, "Fire_signal")}'");
            Check("Output aspect updates metadata value for propagation",
                fn.metadata.First(m => m[0] == "Fire_signal")[1] == "1",
                $"got '{fn.metadata.First(m => m[0] == "Fire_signal")[1]}'");
        }

        // C. unconnected (orphan) aspects are still scriptable
        {
            var d = new XmlDocument();
            d.LoadXml("<FM><Functions><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>2</IDNr><FunctionType>0</FunctionType><IDName>Alarm</IDName></Function></Functions>" +
                      "<Inputs><Input orphan=\"true\"><IDNr>0</IDNr><IDName>Manual trigger</IDName><FunctionIDNr>2</FunctionIDNr></Input></Inputs>" +
                      "<Outputs><Output orphan=\"true\"><IDNr>0</IDNr><IDName>Alarm status</IDName><FunctionIDNr>2</FunctionIDNr></Output></Outputs></FM>");
            var fn = new Function(d.SelectSingleNode("//FM/Functions/Function[IDNr=2]")!);
            fn.Script.SyncAspects(new List<Coupling>(), "2", AspectVariable.DeclaredFromXml(d, "2"));
            Check("Orphan Output aspect available to the script",
                fn.Script.AspectVariables.Any(a => a.Category == AspectCategory.Output && a.Label == "Alarm status"));
            Check("Orphan Input aspect available to the script",
                fn.Script.AspectVariables.Any(a => a.Category == AspectCategory.Input && a.Label == "Manual trigger"));
        }

        // D. Global-scoped variable declared in the function table
        {
            var fn = MakeFn("3", "Dispatch crew", "Total := Total + 1;\nresult := Total;");
            fn.Script.Variables.Add(new ScriptVariable { Name = "Total", Scope = ScriptScope.Global, Type = ScriptVarType.INT, InitialValue = "0" });
            fn.Script.Variables.Add(new ScriptVariable { Name = "result", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true });

            scripting.Reset();
            var c1 = scripting.ExecuteCycle(fn, 0);
            scripting.ExecuteCycle(fn, 1);
            Check("Global variable assignment has no runtime error",
                c1 is not null && !c1.RuntimeIssues.Any(i => i.Severity == "Error"),
                string.Join("|", c1?.RuntimeIssues.Select(i => i.Message) ?? Enumerable.Empty<string>()));
            Check("Global variable is shared and increments (=2)",
                Var(fn, "result") == "2", $"got '{Var(fn, "result")}'");
        }

        // E. Static counter persists across cycles
        {
            var fn = MakeFn("4", "Count", "Count := Count + 1;\nresult := Count;");
            fn.Script.Variables.Add(new ScriptVariable { Name = "Count", Scope = ScriptScope.Static, Type = ScriptVarType.INT, InitialValue = "0" });
            fn.Script.Variables.Add(new ScriptVariable { Name = "result", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true });

            scripting.Reset();
            scripting.ExecuteCycle(fn, 0);
            scripting.ExecuteCycle(fn, 1);
            scripting.ExecuteCycle(fn, 2);
            Check("Static counter = 3 after 3 cycles",
                Var(fn, "result") == "3", $"got '{Var(fn, "result")}'");
        }

        // F. full model end-to-end
        {
            var modelPath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_Scripting_Example.xfmv");
            Assert.True(File.Exists(modelPath), $"Example model not copied to test output: {modelPath}");
            var doc = new XmlDocument();
            doc.Load(modelPath);
            var functions = doc.SelectNodes("//FM/Functions/Function")!.Cast<XmlNode>().Select(n => new Function(n)).ToList();
            var byId = functions.ToDictionary(f => f.IDNr);
            var couplings = AspectVariable.CouplingsFromXml(doc);
            foreach (var fn in functions)
            {
                fn.Script.SyncAspects(couplings, fn.IDNr, AspectVariable.DeclaredFromXml(doc, fn.IDNr));
            }

            void Propagate(Function outFn, Coupling c)
            {
                if (!byId.TryGetValue(c.toFn, out var nextFn)) return;
                foreach (var m in outFn.metadata)
                {
                    nextFn.newVariable($"{c.toType}{outFn.IDNr}_{m[0]}", new[] { m[1], m[1] });
                }
            }

            var svc = new ScriptingService(NullLogger<ScriptingService>.Instance);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                foreach (var fn in functions)
                {
                    svc.ExecuteCycle(fn, cycle);
                    foreach (var c in couplings.Where(c => c.outputFn == fn.IDNr)) Propagate(fn, c);
                }
            }

            Check("Detect fire -> Fire_signal = 1", Var(byId["0"], "Fire_signal") == "1", $"got {Var(byId["0"], "Fire_signal")}");
            Check("Assess severity -> Severity_assessment = 1", Var(byId["1"], "Severity_assessment") == "1", $"got {Var(byId["1"], "Severity_assessment")}");
            Check("Alarm occupants -> Alarm_status = 1", Var(byId["2"], "Alarm_status") == "1", $"got {Var(byId["2"], "Alarm_status")}");
            Check("Dispatch crew -> Dispatch_order = 1", Var(byId["3"], "Dispatch_order") == "1", $"got {Var(byId["3"], "Dispatch_order")}");
        }

        // G. host getters/setters accept human labels; FOR keeps declared scope
        {
            var fn = MakeFn("5", "Host test", "in1 := GetInput(\"Fire signal\");\nSetOutput(\"Hazard signal\", 4.0);");
            fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Fire signal", Type = ScriptVarType.REAL });
            fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Output, Label = "Hazard signal", Type = ScriptVarType.REAL });
            fn.Script.Variables.Add(new ScriptVariable { Name = "in1", Scope = ScriptScope.Temp, Type = ScriptVarType.REAL, InitialValue = "0", IsOutput = true });
            fn.metadata.Add(new[] { "Hazard_signal", "0", "", "", "0" });
            fn.newVariable("I0_Fire_signal", new[] { "3.5", "propagated" });

            scripting.ExecuteCycle(fn, 0);
            Check("GetInput with a human label resolves the propagated value",
                Var(fn, "in1") == "3.5", $"got '{Var(fn, "in1")}'");
            Check("SetOutput with a human label reaches the producer metadata",
                fn.metadata.First(m => m[0] == "Hazard_signal")[1] == "4",
                $"got '{fn.metadata.First(m => m[0] == "Hazard_signal")[1]}'");
        }
        {
            var fn = MakeFn("6", "Loop scope", "r := c;\nFOR c := 1 TO 3 DO END_FOR;");
            fn.Script.Variables.Add(new ScriptVariable { Name = "c", Scope = ScriptScope.Static, Type = ScriptVarType.INT, InitialValue = "0" });
            fn.Script.Variables.Add(new ScriptVariable { Name = "r", Scope = ScriptScope.Temp, Type = ScriptVarType.INT, InitialValue = "0", IsOutput = true });

            scripting.Reset();
            scripting.ExecuteCycle(fn, 0);
            scripting.ExecuteCycle(fn, 1);
            Check("FOR over a Static variable keeps its declared lifetime (r=3 in cycle 2)",
                Var(fn, "r") == "3", $"got '{Var(fn, "r")}'");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} failed: {string.Join("; ", failures)}");
    }
}
