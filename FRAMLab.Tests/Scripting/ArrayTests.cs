using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>
/// Array support in the ST dialect: declarations (ARRAY[lo..hi] OF type),
/// indexed reads/writes, array literals, bounds checks and Temp/Static/Global
/// lifetime behaviour.
/// </summary>
public class ArrayTests
{
    private static Function NewFunction(string source, string id = "1", params ScriptVariable[] variables)
    {
        var doc = new XmlDocument();
        doc.LoadXml(
            "<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\" groupType=\"\" groupIDNr=\"\"" +
            " groupActive=\"\" groupX=\"\" groupY=\"\">" +
            $"<IDNr>{id}</IDNr><FunctionType>0</FunctionType><IDName>f{id}</IDName>" +
            "</Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.Source = source;
        foreach (var v in variables) fn.Script.Variables.Add(v);
        return fn;
    }

    private static ScriptVariable Array(string name, ScriptVarType type, ScriptScope scope,
        string init, int lower, int upper, bool output = false)
        => new()
        {
            Name = name,
            Type = type,
            Scope = scope,
            InitialValue = init,
            IsArray = true,
            LowerBound = lower,
            UpperBound = upper,
            IsOutput = output,
        };

    private static ScriptVariable Scalar(string name, ScriptVarType type, ScriptScope scope,
        string init, bool output = false)
        => new() { Name = name, Type = type, Scope = scope, InitialValue = init, IsOutput = output };

    private static ScriptVariable UninitializedArray(string name, ScriptVarType type, ScriptScope scope = ScriptScope.Temp)
        => new() { Name = name, Type = type, Scope = scope, InitialValue = "", IsArray = true };

    private static ScriptingService NewService() => new(NullLogger<ScriptingService>.Instance);

    private static string? Var(Function fn, string key)
    {
        var vars = fn.getVariables();
        return vars.TryGetValue(key, out var value) && value.Length > 0 ? value[0] : null;
    }

    [Fact]
    public void Parser_ParsesIndexedReadsAndWrites()
    {
        var lexer = new STLexer("Samples[2] := 5.0; x := Samples[2] + 1.0;");
        var parser = new STParser(lexer.Tokenize());
        var program = parser.ParseProgram();

        Assert.Empty(parser.Diagnostics);

        var write = Assert.IsType<Assignment>(program.Statements[0]);
        Assert.Equal("Samples", write.Target);
        Assert.NotNull(write.Index);

        var read = Assert.IsType<Assignment>(program.Statements[1]);
        var add = Assert.IsType<BinaryExpr>(read.Value);
        var index = Assert.IsType<IndexExpr>(add.Left);
        Assert.Equal("Samples", Assert.IsType<IdentifierExpr>(index.Target).Name);
    }

    [Fact]
    public void Compiler_RejectsIndexingAScalar()
    {
        var fn = NewFunction("scalar[0] := 1;", variables: Scalar("scalar", ScriptVarType.REAL, ScriptScope.Temp, "0"));

        var compiled = NewService().Compile(fn);

        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains("not an array"));
    }

    [Fact]
    public void Compiler_AllowsWritingAnyIndex_ButRejectsOverflow()
    {
        var ok = NewFunction("arr[500] := 1;", variables: UninitializedArray("arr", ScriptVarType.INT));
        Assert.False(NewService().Compile(ok).HasErrors);

        var overflow = NewFunction("arr[100000] := 1;", variables: UninitializedArray("arr", ScriptVarType.INT));
        var compiled = NewService().Compile(overflow);
        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains("Index 100000"));
    }

    [Fact]
    public void Compiler_RejectsNegativeConstantIndex()
    {
        var fn = NewFunction("x := arr[-1];",
            variables: new[]
            {
                Array("arr", ScriptVarType.INT, ScriptScope.Temp, "0", 0, 0),
                Scalar("x", ScriptVarType.INT, ScriptScope.Temp, "0"),
            });

        var compiled = NewService().Compile(fn);

        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains("Index -1") && d.Message.Contains("0 to 99999"));
    }

    [Fact]
    public void Interpreter_WritesIndexedElementsAndSumsThem()
    {
        var fn = NewFunction(
            "arr[0] := 1; arr[1] := 2; arr[2] := 3;" +
            "total := 0; FOR i := 0 TO 2 DO total := total + arr[i]; END_FOR;",
            variables: new[]
            {
                Array("arr", ScriptVarType.INT, ScriptScope.Static, "0", 0, 2),
                Scalar("total", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
            });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.Equal("6", Var(fn, "total"));
    }

    [Fact]
    public void Interpreter_AssignsArrayLiteralsWithTypeConversion()
    {
        var fn = NewFunction(
            "wave := [1, 2, 3, 4]; total := wave[0] + wave[3];",
            variables: new[]
            {
                Array("wave", ScriptVarType.INT, ScriptScope.Temp, "0", 0, 3),
                Scalar("total", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
            });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.Equal("5", Var(fn, "total"));
    }

    [Fact]
    public void Interpreter_StaticArrayPersistsAcrossCycles()
    {
        var fn = NewFunction(
            "counters[slot] := counters[slot] + 1; slot := (slot + 1) MOD 3;" +
            "total := counters[0] + counters[1] + counters[2];",
            variables: new[]
            {
                Array("counters", ScriptVarType.INT, ScriptScope.Static, "0", 0, 2),
                Scalar("slot", ScriptVarType.INT, ScriptScope.Static, "0"),
                Scalar("total", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
            });

        var svc = NewService();
        svc.Reset();
        svc.ExecuteCycle(fn, 0);
        svc.ExecuteCycle(fn, 1);
        svc.ExecuteCycle(fn, 2);

        Assert.Equal("3", Var(fn, "total"));
    }

    [Fact]
    public void Interpreter_GlobalArrayIsSharedBetweenFunctions()
    {
        var writer = NewFunction("Shared[0] := 7;", id: "1",
            variables: Array("Shared", ScriptVarType.INT, ScriptScope.Global, "0", 0, 2));
        var reader = NewFunction("read := Shared[0];", id: "2",
            variables: new[]
            {
                Array("Shared", ScriptVarType.INT, ScriptScope.Global, "0", 0, 2),
                Scalar("read", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
            });

        var svc = NewService();
        svc.Reset();
        svc.ExecuteCycle(writer, 0);
        svc.ExecuteCycle(reader, 1);

        Assert.Equal("7", Var(reader, "read"));
    }

    [Fact]
    public void Compiler_RejectsReadingAnUninitializedVariable()
    {
        var fn = NewFunction("y := x + 1;", variables: new[]
        {
            Scalar("x", ScriptVarType.INT, ScriptScope.Temp, ""),
            Scalar("y", ScriptVarType.INT, ScriptScope.Temp, "0"),
        });

        var compiled = NewService().Compile(fn);

        Assert.True(compiled.HasErrors);
        Assert.Contains(compiled.Diagnostics, d => d.Message.Contains("'x' is used before it is initialized"));
    }

    [Fact]
    public void Compiler_AllowsValueAssignedBeforeReadWithoutInitialValue()
    {
        var fn = NewFunction("x := 5; y := x + 1;", variables: new[]
        {
            Scalar("x", ScriptVarType.INT, ScriptScope.Temp, ""),
            Scalar("y", ScriptVarType.INT, ScriptScope.Temp, ""),
        });

        var compiled = NewService().Compile(fn);

        Assert.False(compiled.HasErrors);
    }

    [Fact]
    public void Array_WithoutInitialValue_StartsEmpty_AndGrowsOnWrite()
    {
        var fn = NewFunction("arr[3] := 7; n := arr[3] + arr[0];", variables: new[]
        {
            UninitializedArray("arr", ScriptVarType.INT),
            Scalar("n", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
        });

        var compiled = NewService().Compile(fn);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));

        var ctx = NewService().ExecuteCycle(fn, 0);
        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.Equal("7", Var(fn, "n")); // gaps (arr[0..2]) were filled with 0
    }

    [Fact]
    public void Interpreter_ArraySizeComesFromLiteralWhenNoInitialValue()
    {
        var fn = NewFunction("arr := [10, 20, 30]; total := arr[0] + arr[2];", variables: new[]
        {
            UninitializedArray("arr", ScriptVarType.INT),
            Scalar("total", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
        });

        var svc = NewService();
        var compiled = svc.Compile(fn);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        var ctx = svc.ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.Equal("40", Var(fn, "total"));
    }

    [Fact]
    public void Runtime_ReportsReadOfElementNeverWritten()
    {
        var fn = NewFunction("arr[1] := 5; x := arr[4];", variables: new[]
        {
            UninitializedArray("arr", ScriptVarType.INT),
            Scalar("x", ScriptVarType.INT, ScriptScope.Temp, "0"),
        });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.Contains(ctx!.RuntimeIssues, i => i.Severity == "Error" && i.Message.Contains("'arr[4]' has not been written yet"));
    }

    [Fact]
    public void WholeArrayAssignment_ReplacesContents_WhateverTheLength()
    {
        var fn = NewFunction("arr := [1, 2, 3, 4, 5]; n := arr[4]; arr := []; arr[0] := 9; m := arr[0];", variables: new[]
        {
            Array("arr", ScriptVarType.INT, ScriptScope.Temp, "1,2", 0, 0),
            Scalar("n", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
            Scalar("m", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
        });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.Equal("5", Var(fn, "n"));
        Assert.Equal("9", Var(fn, "m"));
    }

    [Fact]
    public void Interpreter_HonoursCustomLowerBound()
    {
        var fn = NewFunction(
            "arr[1] := 10; arr[3] := 30; total := arr[1] + arr[3];",
            variables: new[]
            {
                Array("arr", ScriptVarType.INT, ScriptScope.Temp, "0", 1, 3),
                Scalar("total", ScriptVarType.INT, ScriptScope.Temp, "0", output: true),
            });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        Assert.Equal("40", Var(fn, "total"));
    }

    [Fact]
    public void Interpreter_ReportsOverflowingIndex()
    {
        var fn = NewFunction("idx := 100000; arr[idx] := 1;",
            variables: new[]
            {
                UninitializedArray("arr", ScriptVarType.INT),
                Scalar("idx", ScriptVarType.INT, ScriptScope.Temp, "0"),
            });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.Contains(ctx!.RuntimeIssues,
            i => i.Severity == "Error" && i.Message.Contains("Index 100000") && i.Message.Contains("0 to 99999"));
    }

    [Fact]
    public void ScriptModel_RoundTripsArrayDeclaration()
    {
        var doc = new XmlDocument();
        doc.LoadXml(
            "<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\" groupType=\"\" groupIDNr=\"\"" +
            " groupActive=\"\" groupX=\"\" groupY=\"\"><IDNr>9</IDNr><FunctionType>0</FunctionType>" +
            "<IDName>roundtrip</IDName></Function></FM>");
        var node = doc.SelectSingleNode("//Function")!;

        var model = new ScriptModel { Source = "arr[1] := 2;" };
        model.Variables.Add(Array("arr", ScriptVarType.REAL, ScriptScope.Static, "0.0", -2, 2));
        model.WriteXml(doc, node);

        var reloaded = ScriptModel.FromXml(node);
        var arr = Assert.Single(reloaded.Variables);

        Assert.True(arr.IsArray);
        Assert.Equal(-2, arr.LowerBound);
        Assert.Equal(2, arr.UpperBound);
        Assert.Equal(ScriptVarType.REAL, arr.Type);
        Assert.Equal(ScriptScope.Static, arr.Scope);
    }

    [Fact]
    public void ExampleModel_ArrayUseCase_Works()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_Array_Example.xfmv");
        Assert.True(File.Exists(modelPath), $"Example model not copied to test output: {modelPath}");

        var doc = new XmlDocument();
        doc.Load(modelPath);
        var fn = new Function(doc.SelectSingleNode("//FM/Functions/Function")!);

        var svc = NewService();
        svc.Reset();
        ScriptExecutionContext? last = null;
        for (var cycle = 0; cycle < 5; cycle++)
        {
            last = svc.ExecuteCycle(fn, cycle);
            Assert.NotNull(last);
            Assert.DoesNotContain(last!.RuntimeIssues, i => i.Severity == "Error");
        }

        // Window 0..4 => sum 10, average 2; weighted score = 4.0
        Assert.Equal("2", Var(fn, "Average"));
        Assert.Equal("4", Var(fn, "Weighted_sum"));
        Assert.Equal("[0,1,2,3,4]", Var(fn, "Samples"));
    }
}
