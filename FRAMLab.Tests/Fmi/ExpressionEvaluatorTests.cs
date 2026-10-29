using FRAMLab.Shared.MainApp.Services;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// The legacy metadata-equation evaluator (f(x)= fields): arithmetic, comparisons, logic,
/// built-in functions, variables, CSV lookup/xref and unresolved-variable reporting.
/// Values are strings throughout, as in the evaluator itself.
/// </summary>
public class ExpressionEvaluatorTests
{
    private static readonly Dictionary<string, string[]> Vars = new()
    {
        ["Risk"] = new[] { "0.4", "" },
        ["Count"] = new[] { "3", "" },
        ["Name"] = new[] { "alpha", "" },
        ["Empty"] = new[] { "", "" },
        ["I3_Delay"] = new[] { "2", "" },
    };

    private static string Eval(string expression, Dictionary<string, string[]>? vars = null,
        List<string[]>? rows = null, Dictionary<string, string>? keys = null, ICollection<string>? unresolved = null)
        => new ExpressionEvaluator().Eval(expression, vars ?? Vars, rows ?? new List<string[]>(), keys ?? new Dictionary<string, string>(), unresolved);

    [Theory]
    [InlineData("1+2", "3")]
    [InlineData("10-4-3", "3")]
    [InlineData("2*3+4", "10")]
    [InlineData("2+3*4", "14")]
    [InlineData("(2+3)*4", "20")]
    [InlineData("8/2/2", "2")]
    [InlineData("-3+5", "2")]
    [InlineData("2^3", "8")]
    [InlineData("2π", "6.283185307179586")]
    [InlineData("1/0", "Infinity")]
    public void Arithmetic(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Theory]
    [InlineData("3>2", "1")]
    [InlineData("3<2", "0")]
    [InlineData("3>=3", "1")]
    [InlineData("3<=2", "0")]
    [InlineData("3=3", "1")]
    [InlineData("3==4", "0")]
    [InlineData("3<>4", "1")]
    [InlineData("3!=3", "0")]
    [InlineData("1><0", "1")] // XOR
    [InlineData("1><1", "0")]
    [InlineData("1&1", "1")]
    [InlineData("1&&0", "0")]
    [InlineData("0|1", "1")]
    [InlineData("0||0", "0")]
    [InlineData("!0", "1")]
    [InlineData("!5", "0")]
    public void ComparisonsAndLogic(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Fact]
    public void Comparison_BindsTighterThanAddition()
    {
        // Comparisons are parsed at factor level: 2+3>4 is 2+(3>4) = 2. Documented so a
        // future evaluator change is a deliberate decision, not an accident.
        Assert.Equal("2", Eval("2+3>4"));
        Assert.Equal("1", Eval("(2+3)>4"));
    }

    [Theory]
    [InlineData("Risk*10", "4")]
    [InlineData("Count+1", "4")]
    [InlineData("I3_Delay*Count", "6")]
    [InlineData("Empty+1", "1")] // an empty value reads as 0
    [InlineData("Name=\"alpha\"", "1")]
    [InlineData("Risk>0.3?\"high\":\"low\"", "high")]
    public void Variables(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Theory]
    [InlineData("min(3,5)", "3")]
    [InlineData("max(3,5)", "5")]
    [InlineData("round(2.567)", "3")]
    [InlineData("round(2.567,2)", "2.57")]
    [InlineData("sqrt(16)", "4")]
    [InlineData("abs(-2)", "2")]
    [InlineData("ceiling(1.2)", "2")]
    [InlineData("floor(1.8)", "1")]
    [InlineData("int(7.9)", "7")]
    [InlineData("round(sin(30),4)", "0.5")] // degrees
    [InlineData("round(cos(60),4)", "0.5")]
    [InlineData("round(tan(45),4)", "1")]
    [InlineData("exp(0)", "1")]
    [InlineData("log(1)", "0")]
    [InlineData("api(1)", "NaN")] // placeholder until the API is designed
    public void BuiltInFunctions(string expression, string expected) => Assert.Equal(expected, Eval(expression));

    [Fact]
    public void RandomFunctions_StayInRange()
    {
        for (var i = 0; i < 200; i++)
        {
            var rnd = double.Parse(Eval("rnd(2,4)"), System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(rnd, 2, 4);
            var rndi = int.Parse(Eval("rndi(1,3)"));
            Assert.InRange(rndi, 1, 2); // upper bound is exclusive
        }
    }

    [Fact]
    public void LookupAndXref_ReadTheImportedCsv()
    {
        var rows = new List<string[]>
        {
            new[] { "key", "low", "high" },
            new[] { "A", "1", "9" },
        };
        var keys = new Dictionary<string, string> { ["A,high"] = "9", ["A,low"] = "1" };

        Assert.Equal("9", Eval("lookup(1,2)", rows: rows));
        Assert.Equal("NaN", Eval("lookup(5,0)", rows: rows));
        Assert.Equal("1", Eval("xref(\"A\",\"low\")", keys: keys));
        Assert.Equal("NaN", Eval("xref(\"B\",\"low\")", keys: keys));
    }

    [Theory]
    [InlineData("1+")]
    [InlineData("2 3")]
    public void MalformedInput_ReturnsNaN(string expression) => Assert.Equal("NaN", Eval(expression));

    [Fact]
    public void UnresolvedVariables_AreReported_OncePerName()
    {
        var unresolved = new List<string>();

        var result = Eval("Missing + Risk * Missing + Other + 1", unresolved: unresolved);

        Assert.Equal("1", result); // still evaluated with 0 so the caller can decide
        Assert.Equal(new[] { "Missing", "Other" }, unresolved);
    }

    [Fact]
    public void UnresolvedVariables_NotTrackedWithoutCollector()
    {
        Assert.Equal("1", Eval("Missing+1"));
    }
}
