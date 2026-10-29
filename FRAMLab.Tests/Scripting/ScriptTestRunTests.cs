using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>The script editor's "Run one cycle": chosen inputs in, outputs/Static/Print out, no side effects.</summary>
public class ScriptTestRunTests
{
    private static Function NewFunction(string source)
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>f1</IDName>" +
                    "<metadata><Alarm active=\"false\" equation=\"\">0</Alarm></metadata></Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.Source = source;
        fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Smoke level", Type = ScriptVarType.REAL });
        fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Output, Label = "Alarm", Type = ScriptVarType.INT });
        fn.Script.Variables.Add(new ScriptVariable { Name = "Count", Type = ScriptVarType.INT, Scope = ScriptScope.Static, InitialValue = "0" });
        return fn;
    }

    private const string Source = "Count := Count + 1; IF Smoke_level > 0.5 THEN Alarm := 1; ELSE Alarm := 0; END_IF; Print('cycle ', Cycle, ' smoke ', Smoke_level);";

    [Fact]
    public void UsesTheGivenInputsAndState_AndReportsResults()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance);
        var fn = NewFunction(Source);

        var result = service.TestRun(fn, 7,
            new Dictionary<string, string> { ["Smoke_level"] = "0.8" },
            new Dictionary<string, string> { ["Count"] = "41" });

        Assert.False(result.HasErrors, string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.Equal("1", result.Outputs["Alarm"]);
        Assert.Equal("42", result.Statics["Count"]);
        Assert.EndsWith("cycle 7 smoke 0.8", Assert.Single(result.Trace));
    }

    [Fact]
    public void LeavesTheModelAndTheRunStateAlone()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance);
        var fn = NewFunction(Source);
        service.ExecuteCycle(fn, 0); // a real run: Count = 1, Alarm metadata written
        var metadataAfterRun = fn.metadata.Single(m => m[0] == "Alarm")[1];

        var copy = NewFunction(Source);
        service.TestRun(copy, 1, new Dictionary<string, string> { ["Smoke_level"] = "0.9" }, new Dictionary<string, string>());
        fn.Script.Source = "Print(Count);";
        var ctx = service.ExecuteCycle(fn, 1);

        Assert.EndsWith("1", Assert.Single(ctx!.Trace)); // the run's Static store was not touched
        Assert.Equal("0", copy.metadata.Single(m => m[0] == "Alarm")[1]); // test run does not write outputs back
        Assert.Equal(metadataAfterRun, fn.metadata.Single(m => m[0] == "Alarm")[1]);
    }

    [Fact]
    public void CompileErrors_AreReported_WithoutRunning()
    {
        var service = new ScriptingService(NullLogger<ScriptingService>.Instance);
        var fn = NewFunction("Alarm := ;");

        var result = service.TestRun(fn, 1, new Dictionary<string, string>(), new Dictionary<string, string>());

        Assert.True(result.HasErrors);
        Assert.Empty(result.Outputs);
    }
}
