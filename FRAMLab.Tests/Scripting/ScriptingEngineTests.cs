using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Models;
using FRAMLab.Shared.MainApp.Services.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>
/// Smoke tests for the ST language engine after it was ported onto the FMI integration
/// branch. The deeper language/scenario suites still live in FRAMLAB/TestFiles and are
/// converted to xUnit in a later port layer.
/// </summary>
public class ScriptingEngineTests
{
    private static Function NewFunction(string source)
    {
        var doc = new XmlDocument();
        doc.LoadXml(
            "<FM><Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\">" +
            "<IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>f</IDName>" +
            "</Function></FM>");
        var fn = new Function(doc.SelectSingleNode("//Function")!);
        fn.Script.Source = source;
        return fn;
    }

    private static ScriptingService NewService() => new(NullLogger<ScriptingService>.Instance);

    [Fact]
    public void Lexer_ParsesIfComparisonAndAssignment()
    {
        var lexer = new STLexer("IF a >= 2 THEN b := 'x'; END_IF;");
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(TokenKind.If, tokens[0].Kind);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Ge);
        Assert.Contains(tokens, t => t.Kind == TokenKind.Assign);
        Assert.Contains(tokens, t => t.Kind == TokenKind.StringLiteral && t.Text == "x");
    }

    [Fact]
    public void Parser_HonoursMultiplicationPrecedence()
    {
        var lexer = new STLexer("x := 1 + 2 * 3;");
        var parser = new STParser(lexer.Tokenize());
        var program = parser.ParseProgram();

        Assert.Empty(parser.Diagnostics);
        var assignment = Assert.IsType<Assignment>(program.Statements[0]);
        var add = Assert.IsType<BinaryExpr>(assignment.Value);
        Assert.Equal("+", add.Op);
        var multiply = Assert.IsType<BinaryExpr>(add.Right);
        Assert.Equal("*", multiply.Op);
    }

    [Fact]
    public void Compiler_RejectsUndeclaredVariable()
    {
        var fn = NewFunction("unknownVariable := 1;");

        var compiled = NewService().Compile(fn);

        Assert.True(compiled.HasErrors);
    }

    [Fact]
    public void Interpreter_RunsScriptAndPrintsDeclaredVariable()
    {
        var fn = NewFunction("result := 40 + 2; Print('result=', result);");
        fn.Script.Variables.Add(new ScriptVariable
        {
            Name = "result",
            Scope = ScriptScope.Temp,
            Type = ScriptVarType.REAL,
            InitialValue = "0"
        });

        var ctx = NewService().ExecuteCycle(fn, 0);

        Assert.NotNull(ctx);
        Assert.Contains(ctx!.Trace, trace => trace.Contains("result=42"));
    }
}
