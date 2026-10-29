using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>
/// Regression tests for scripting bugs: EXIT in WHILE/REPEAT, case-insensitive host
/// functions, TIME initial values, script outputs preserving the metadata initial value,
/// and persistence of the IDE's model-wide globals.
/// </summary>
public class ScriptBugfixTests
{
    private static Function NewFunction(string source, string metadataXml = "", params ScriptVariable[] variables)
    {
        var doc = new XmlDocument();
        doc.LoadXml(
            "<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\" groupType=\"\" groupIDNr=\"\"" +
            " groupActive=\"\" groupX=\"\" groupY=\"\">" +
            "<IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>f1</IDName>" +
            metadataXml +
            "</Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.Source = source;
        foreach (var v in variables) fn.Script.Variables.Add(v);
        return fn;
    }

    private static ScriptVariable Scalar(string name, ScriptVarType type, string init, bool output = false)
        => new() { Name = name, Type = type, Scope = ScriptScope.Temp, InitialValue = init, IsOutput = output };

    private static ScriptExecutionContext Run(Function fn)
    {
        var ctx = new ScriptingService(NullLogger<ScriptingService>.Instance).ExecuteCycle(fn, 0);
        Assert.NotNull(ctx);
        Assert.DoesNotContain(ctx!.RuntimeIssues, i => i.Severity == "Error");
        return ctx;
    }

    [Fact]
    public void Exit_InWhile_LeavesOnlyTheLoop()
    {
        var fn = NewFunction(
            "WHILE TRUE DO i := i + 1; IF i >= 3 THEN EXIT; END_IF; END_WHILE; Print('after=', i);",
            "", Scalar("i", ScriptVarType.INT, "0"));

        var ctx = Run(fn);

        Assert.Contains(ctx.Trace, t => t.EndsWith("after=3"));
    }

    [Fact]
    public void Exit_InRepeat_LeavesOnlyTheLoop()
    {
        var fn = NewFunction(
            "REPEAT i := i + 1; IF i = 2 THEN EXIT; END_IF; UNTIL i >= 10 END_REPEAT; Print('after=', i);",
            "", Scalar("i", ScriptVarType.INT, "0"));

        var ctx = Run(fn);

        Assert.Contains(ctx.Trace, t => t.EndsWith("after=2"));
    }

    [Fact]
    public void HostFunctions_AreCaseInsensitive()
    {
        var fn = NewFunction("print('hello'); x := cycle();", "", Scalar("x", ScriptVarType.INT, "0"));

        var compiled = new ScriptingService(NullLogger<ScriptingService>.Instance).Compile(fn);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));

        var ctx = Run(fn);
        Assert.Contains(ctx.Trace, t => t.EndsWith("hello"));
    }

    [Fact]
    public void TimeVariable_UsesItsInitialValue()
    {
        var fn = NewFunction("IF t = T#5s THEN Print('ok'); END_IF;", "", Scalar("t", ScriptVarType.TIME, "T#5s"));

        var ctx = Run(fn);

        Assert.Contains(ctx.Trace, t => t.EndsWith("ok"));
    }

    [Theory]
    [InlineData("T#250ms")]
    [InlineData("t#250ms")]
    [InlineData("TIME#250ms")]
    public void TimeLiterals_Lex(string literal)
    {
        var lexer = new STLexer($"x := {literal};");
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Contains(tokens, t => t.Kind == TokenKind.TimeLiteral && t.Text == literal);
    }

    [Fact]
    public void Var_ReportsClearError()
    {
        var fn = NewFunction("VAR x : INT; END_VAR");
        var compiled = new ScriptingService(NullLogger<ScriptingService>.Instance).Compile(fn);
        Assert.Contains(compiled.Diagnostics, d => d.Message.StartsWith("VAR sections are not supported"));
    }

    [Fact]
    public void ScriptOutput_UpdatesValue_ButKeepsMetadataInitialValue()
    {
        var fn = NewFunction("Score := 9.0;",
            "<metadata><Score active=\"false\" equation=\"\">1</Score></metadata>",
            Scalar("Score", ScriptVarType.REAL, "0", output: true));

        Run(fn);

        var row = fn.metadata.Single(m => m[0] == "Score");
        Assert.Equal("9", row[1]);
        Assert.Equal("1", row[4]); // FMI Reset restores [1] from [4]
    }

    [Fact]
    public void Globals_RoundTripThroughTheModel()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Functions/></FM>");
        var globals = new List<ScriptVariable>
        {
            new() { Name = "Total", Type = ScriptVarType.REAL, InitialValue = "0" },
            new() { Name = "Hist", Type = ScriptVarType.INT, InitialValue = "1,2,3", IsArray = true },
        };

        ScriptModel.WriteGlobals(doc, globals);
        var read = ScriptModel.ReadGlobals(doc);

        Assert.Equal(new[] { "Total", "Hist" }, read.Select(g => g.Name));
        Assert.All(read, g => Assert.Equal(ScriptScope.Global, g.Scope));
        Assert.Equal("1,2,3", read[1].InitialValue);
        Assert.True(read[1].IsArray);

        // no globals -> no node, so models without globals save unchanged
        ScriptModel.WriteGlobals(doc, new List<ScriptVariable>());
        Assert.Null(doc.SelectSingleNode("//FM/ScriptGlobals"));
    }
}

/// <summary>Aspect reads must use the coupling variable (I6_Key), never an aggregate (In_Key, Isum_Key).</summary>
public class AspectReadTests
{
    [Fact]
    public void AspectVariable_And_GetInput_IgnoreAggregates()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>9</IDNr><FunctionType>0</FunctionType><IDName>f9</IDName></Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.AspectVariables.Add(new AspectVariable { Category = AspectCategory.Input, Label = "Loaded trucks", Type = ScriptVarType.INT });
        fn.Script.Source = "Print(Loaded_trucks, ' ', GetInput('Loaded_trucks'));";
        // aggregates listed before the coupling variable, as after an input was consumed and re-delivered
        fn.newVariable("In_Loaded_trucks", new[] { "1" });
        fn.newVariable("Isum_Loaded_trucks", new[] { "4" });
        fn.newVariable("I6_Loaded_trucks", new[] { "4" });

        var ctx = new ScriptingService(NullLogger<ScriptingService>.Instance).ExecuteCycle(fn, 1);

        Assert.EndsWith("4 4", Assert.Single(ctx!.Trace));
    }
}
