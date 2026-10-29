using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>CASE ... OF ... END_CASE: single labels, lists, ranges, negatives, ELSE, and checks.</summary>
public class CaseTests
{
    private const string Program = """
        CASE level OF
          1:       label := 'low';
          2, 3:    label := 'medium';
          4..9:    label := 'high';
          -1:      label := 'off';
        ELSE
          label := 'unknown';
        END_CASE;
        Print(label);
        """;

    private static Function NewFunction(string source, params ScriptVariable[] variables)
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>f1</IDName></Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.Source = source;
        foreach (var v in variables) fn.Script.Variables.Add(v);
        return fn;
    }

    private static ScriptingService NewService() => new(NullLogger<ScriptingService>.Instance);

    [Theory]
    [InlineData("1", "low")]
    [InlineData("2", "medium")]
    [InlineData("3", "medium")]
    [InlineData("4", "high")]
    [InlineData("9", "high")]
    [InlineData("-1", "off")]
    [InlineData("10", "unknown")]
    [InlineData("0", "unknown")]
    public void SelectsTheMatchingBranch(string level, string expected)
    {
        var fn = NewFunction(Program,
            new ScriptVariable { Name = "level", Type = ScriptVarType.INT, InitialValue = level },
            new ScriptVariable { Name = "label", Type = ScriptVarType.STRING, InitialValue = "" });

        var compiled = NewService().Compile(fn);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.EndsWith(expected, Assert.Single(ctx.Trace));
    }

    [Fact]
    public void WithoutElse_NoBranchRuns()
    {
        var fn = NewFunction("x := 0; CASE n OF 1: x := 1; END_CASE; Print(x);",
            new ScriptVariable { Name = "n", Type = ScriptVarType.INT, InitialValue = "5" },
            new ScriptVariable { Name = "x", Type = ScriptVarType.INT, InitialValue = "0" });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.EndsWith("0", Assert.Single(ctx!.Trace));
    }

    [Fact]
    public void Exit_InsideCase_LeavesTheEnclosingLoop()
    {
        var fn = NewFunction("FOR i := 1 TO 10 DO CASE i OF 3: EXIT; END_CASE; n := i; END_FOR; Print(n);",
            new ScriptVariable { Name = "i", Type = ScriptVarType.INT, InitialValue = "0" },
            new ScriptVariable { Name = "n", Type = ScriptVarType.INT, InitialValue = "0" });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.EndsWith("2", Assert.Single(ctx!.Trace));
    }

    [Theory]
    [InlineData("CASE n OF 1: x := 1; 1: x := 2; END_CASE;", "label 1 is already covered by 1")]
    [InlineData("CASE n OF 1..5: x := 1; 4: x := 2; END_CASE;", "label 4 is already covered by 1..5")]
    [InlineData("CASE n OF a: x := 1; END_CASE;", "whole-number constants")]
    [InlineData("CASE n OF 5..1: x := 1; END_CASE;", "is empty")]
    [InlineData("CASE n OF 1: x := 1;", "Expected END_CASE")]
    public void ReportsMistakes(string source, string message)
    {
        var fn = NewFunction(source,
            new ScriptVariable { Name = "n", Type = ScriptVarType.INT, InitialValue = "1" },
            new ScriptVariable { Name = "x", Type = ScriptVarType.INT, InitialValue = "0" });

        var compiled = NewService().Compile(fn);

        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains(message));
    }

    [Fact]
    public void InitializationAnalysis_RequiresEveryBranch()
    {
        // y is only assigned in one branch and there is no ELSE: reading it afterwards is an error
        var fn = NewFunction("CASE n OF 1: y := 1; END_CASE; z := y;",
            new ScriptVariable { Name = "n", Type = ScriptVarType.INT, InitialValue = "1" },
            new ScriptVariable { Name = "y", Type = ScriptVarType.INT, InitialValue = "" },
            new ScriptVariable { Name = "z", Type = ScriptVarType.INT, InitialValue = "0" });

        var compiled = NewService().Compile(fn);

        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains("'y' is used before it is initialized"));
    }
}
