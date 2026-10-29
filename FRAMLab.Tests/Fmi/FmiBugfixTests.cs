using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Regression tests for FMI bugs: nested ternaries in the legacy evaluator, macros being
/// used during a simulation run, and execute() with an unknown method.
/// </summary>
public class FmiBugfixTests
{
    private const string FnXml = "<Function fnStyle=\"0\" orphans=\"0\" x=\"0\" y=\"0\"><IDNr>1</IDNr><FunctionType>0</FunctionType><IDName>F1</IDName>{0}</Function>";

    private static string Eval(string expression)
        => new ExpressionEvaluator().Eval(expression, new Dictionary<string, string[]>(), new List<string[]>(), new Dictionary<string, string>());

    [Theory]
    [InlineData("1 ? 5 : 7", "5")]
    [InlineData("0 ? 5 : 7", "7")]
    [InlineData("1 ? (0 ? 5 : 6) : 7", "6")]
    [InlineData("1 ? 0 ? 5 : 6 : 7", "6")]
    [InlineData("0 ? 1 : 0 ? 2 : 3", "3")]
    [InlineData("(1 ? 2 : 3) + 10", "12")]
    [InlineData("(0 ? 2 : 3) + 10", "13")]
    [InlineData("max(1 ? 4 : 9, 2)", "4")]
    [InlineData("1 ? \"yes\" : \"no\"", "yes")]
    [InlineData("0 ? \"a:b\" : \"no\"", "no")]
    public void Ternary_HandlesNestingParenthesesAndStrings(string expression, string expected)
    {
        Assert.Equal(expected, Eval(expression));
    }

    [Fact]
    public void Macro_IsUsedByEquationsDuringARun()
    {
        var xml = $"<FM><Functions>{string.Format(FnXml, "<metadata><Score active=\"true\" equation=\"doubleIt(3) + 1\">0</Score></metadata>")}</Functions>" +
                  "<FMI version=\"1\"><Variables></Variables><Rules></Rules>" +
                  "<Macros><Macro name=\"doubleIt\" kind=\"formula\" return=\"number\"><Param name=\"x\" type=\"number\" /><Body>x * 2</Body></Macro></Macros>" +
                  "</FMI></FM>";
        var (interpreter, _, functions) = InterpreterFixture.FromXml(xml);

        InterpreterFixture.RunCycle(interpreter, functions, 0);

        Assert.Equal("7", functions[0].metadata.Single(m => m[0] == "Score")[1]);
        Assert.Empty(interpreter.PendingDiagnostics);
    }

    [Fact]
    public void Execute_WithUnknownMethod_WarnsAndKeepsValue()
    {
        var xml = $"<FM><Functions>{string.Format(FnXml, "<metadata><P active=\"true\" equation=\"Execute(PBBN)\">0.4</P></metadata>")}</Functions></FM>";
        var (interpreter, _, functions) = InterpreterFixture.FromXml(xml);

        var results = InterpreterFixture.RunCycle(interpreter, functions, 0);

        Assert.Equal("0.4", functions[0].metadata.Single(m => m[0] == "P")[1]);
        Assert.Contains(results[0].Diagnostics, d => d.Severity == FmiDiagnosticSeverity.Warning && d.Message.Contains("PBBN"));
    }
}
