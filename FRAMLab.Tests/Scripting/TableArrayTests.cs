using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>2-D arrays (tables): m[row, col] grows rows and columns together, stays rectangular,
/// gaps are zeros, unwritten reads are errors, and [[...], [...]] literals.</summary>
public class TableArrayTests
{
    private static Function NewFunction(string source, params ScriptVariable[] variables)
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>f1</IDName></Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.Source = source;
        foreach (var v in variables) fn.Script.Variables.Add(v);
        return fn;
    }

    private static ScriptVariable Table(string name, string init = "", ScriptScope scope = ScriptScope.Temp)
        => new() { Name = name, Type = ScriptVarType.INT, IsArray = true, Dimensions = 2, InitialValue = init, Scope = scope };

    private static ScriptVariable Int(string name, string init = "0")
        => new() { Name = name, Type = ScriptVarType.INT, InitialValue = init };

    private static ScriptingService NewService() => new(NullLogger<ScriptingService>.Instance);

    private static ScriptExecutionContext RunClean(Function fn)
    {
        var compiled = NewService().Compile(fn);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        var ctx = NewService().ExecuteCycle(fn, 0);
        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        return ctx;
    }

    [Fact]
    public void Write_GrowsRowsAndColumns_AndFillsGapsWithZero()
    {
        var fn = NewFunction("m[1, 2] := 7; m[0, 0] := 1; Print(m);", Table("m"));

        var ctx = RunClean(fn);

        Assert.EndsWith("[[1,0,0],[0,0,7]]", Assert.Single(ctx.Trace));
    }

    [Fact]
    public void Write_ToAWiderColumn_WidensEveryRow()
    {
        var fn = NewFunction("m := [[1, 2], [3, 4]]; m[0, 3] := 9; Print(m);", Table("m"));

        var ctx = RunClean(fn);

        Assert.EndsWith("[[1,2,0,9],[3,4,0,0]]", Assert.Single(ctx.Trace));
    }

    [Fact]
    public void InitialValue_RowsSeparatedBySemicolons_AndShortRowsPadded()
    {
        var fn = NewFunction("Print(m[1, 1], ' ', m[0, 2]);", Table("m", "1,2,3;4,5"));

        var ctx = RunClean(fn);

        Assert.EndsWith("5 3", Assert.Single(ctx.Trace));
    }

    [Fact]
    public void NestedLoops_FillATable()
    {
        var fn = NewFunction("FOR i := 0 TO 2 DO FOR j := 0 TO 2 DO m[i, j] := i * j; END_FOR; END_FOR; Print(m[2, 2]);",
            Table("m"), Int("i"), Int("j"));

        var ctx = RunClean(fn);

        Assert.EndsWith("4", Assert.Single(ctx.Trace));
    }

    [Fact]
    public void Read_OfCellNeverWritten_IsAnError()
    {
        var fn = NewFunction("m[0, 0] := 1; x := m[3, 0];", Table("m"), Int("x"));

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.Contains(ctx!.RuntimeIssues, i => i.Severity == "Error" && i.Message.Contains("'m[3, 0]' has not been written yet"));
    }

    [Fact]
    public void Static_Table_KeepsItsCellsBetweenCycles()
    {
        var fn = NewFunction("m[Cycle, 0] := Cycle * 10;", Table("m", scope: ScriptScope.Static));
        var service = NewService();

        for (var cycle = 0; cycle < 3; cycle++) service.ExecuteCycle(fn, cycle);
        fn.Script.Source = "Print(m);";
        var ctx = service.ExecuteCycle(fn, 3);

        Assert.EndsWith("[[0],[10],[20]]", Assert.Single(ctx!.Trace));
    }

    [Theory]
    [InlineData("x := m[1];", "is a 2-D array; write m[row, column]")]
    [InlineData("a[1, 2] := 3;", "is a 1-D array; write a[index]")]
    [InlineData("m := [1, 2, 3];", "write the value as rows")]
    [InlineData("a := [[1, 2], [3, 4]];", "needs a 2-D array")]
    [InlineData("m := [[1, [2]]];", "at most two dimensions")]
    [InlineData("m[-1, 0] := 1;", "Index -1")]
    public void Compiler_ReportsShapeMistakes(string source, string message)
    {
        var fn = NewFunction(source, Table("m"), Int("x"),
            new ScriptVariable { Name = "a", Type = ScriptVarType.INT, IsArray = true });

        var compiled = NewService().Compile(fn);

        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains(message));
    }

    [Fact]
    public void Dimensions_RoundTripThroughXml()
    {
        var model = new ScriptModel { Source = "m[0, 0] := 1;" };
        model.Variables.Add(Table("m", "1,2;3,4"));
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Function><IDNr>1</IDNr></Function></FM>");
        var fnNode = doc.SelectSingleNode("//Function")!;

        model.WriteXml(doc, fnNode);
        var read = ScriptModel.FromXml(fnNode);

        var m = Assert.Single(read.Variables);
        Assert.True(m.IsTable);
        Assert.Equal("1,2;3,4", m.InitialValue);
        Assert.Equal("2", ((XmlElement)fnNode.SelectSingleNode("variables/var")!).GetAttribute("dims"));
    }

    [Theory]
    [InlineData("1,2;3,4", true)]
    [InlineData("[[1,2],[3,4]]", true)]
    [InlineData("1;2;3", true)]
    [InlineData("1,x;3,4", false)]
    public void TableInitialValue_IsValidated(string init, bool valid)
        => Assert.Equal(valid, ScriptVariable.IsValidTableInit(ScriptVarType.INT, init));
}
