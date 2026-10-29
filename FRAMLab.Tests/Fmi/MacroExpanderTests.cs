using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Formula-macro evaluation: arithmetic, variables, built-ins, nested macros, strict
/// arity/type checks, depth/cycle guards, and the
/// scope-aware callee lookup.
/// </summary>
public class MacroExpanderTests
{
    private static FmiMacroDefinition Macro(string name, string body, string functionId = "",
        params (string Name, FmiTypeDescriptor Type)[] parameters)
    {
        var macro = new FmiMacroDefinition { Name = name, Body = body, FunctionId = functionId };
        foreach (var (pn, pt) in parameters)
        {
            macro.Parameters.Add(new FmiMacroParameter { Name = pn, Type = pt });
        }
        return macro;
    }

    private static (string Result, List<FmiDiagnostic> Diagnostics) Expand(
        IEnumerable<FmiMacroDefinition> macros,
        string expression,
        string callingFunctionId = "",
        Dictionary<string, string[]>? variables = null)
    {
        var registry = new MacroRegistry();
        var definition = new FmiModelDefinition();
        definition.Macros.AddRange(macros);
        registry.LoadFrom(definition);

        var expander = new MacroExpander(registry);
        var diagnostics = new List<FmiDiagnostic>();
        var result = expander.Expand(expression, variables ?? new Dictionary<string, string[]>(), callingFunctionId,
            new FmiEvaluationContext(), diagnostics);
        return (result, diagnostics);
    }

    [Fact]
    public void Formula_EvaluatesArithmetic()
    {
        var (result, diagnostics) = Expand(
            new[] { Macro("doubleIt", "x * 2", "", ("x", FmiTypeDescriptor.Number)) },
            "doubleIt(4)");

        Assert.Equal("8", result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Formula_ResolvesCallerVariables()
    {
        var (result, diagnostics) = Expand(
            new[] { Macro("readX", "X + 1") },
            "readX()",
            variables: new Dictionary<string, string[]> { ["X"] = new[] { "5" } });

        Assert.Equal("6", result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Formula_TernaryAndBuiltIns()
    {
        var (result, diagnostics) = Expand(
            new[] { Macro("hyp", "sqrt(a^2 + b^2)", "",
                ("a", FmiTypeDescriptor.Number), ("b", FmiTypeDescriptor.Number)) },
            "hyp(3, 4)");

        Assert.Equal("5", result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Formula_NestedMacros()
    {
        var (result, diagnostics) = Expand(
            new[]
            {
                Macro("inc", "x + 1", "", ("x", FmiTypeDescriptor.Number)),
                Macro("inc2", "inc(inc(x))", "", ("x", FmiTypeDescriptor.Number))
            },
            "inc2(1)");

        Assert.Equal("3", result);
        Assert.Empty(diagnostics);
    }


    [Fact]
    public void ArityMismatch_EmitsError()
    {
        var (result, diagnostics) = Expand(
            new[] { Macro("f", "x", "", ("x", FmiTypeDescriptor.Number)) },
            "f(1, 2)");

        Assert.Equal("", result);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("expects 1"));
    }

    [Fact]
    public void TypeMismatch_IsHardError()
    {
        var (result, diagnostics) = Expand(
            new[] { Macro("f", "x", "", ("x", FmiTypeDescriptor.Number)) },
            "f(true)");

        Assert.Equal("", result);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("expects number"));
    }

    [Fact]
    public void UnknownMacro_EmitsError()
    {
        var (result, diagnostics) = Expand(Array.Empty<FmiMacroDefinition>(), "nope(1)");

        Assert.Equal("", result);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("Unknown macro 'nope'"));
    }

    [Fact]
    public void UnknownCallee_InLargerExpression_FailsSoft()
    {
        var (result, diagnostics) = Expand(Array.Empty<FmiMacroDefinition>(), "1 + nope(1)");

        Assert.Equal("NaN", result);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("Unknown macro 'nope'"));
    }

    [Fact]
    public void FunctionScopedMacro_ResolvesByCaller()
    {
        var macros = new[] { Macro("g", "x + 100", "3", ("x", FmiTypeDescriptor.Number)) };

        var (inScope, inScopeDiags) = Expand(macros, "g(2)", callingFunctionId: "3");
        Assert.Equal("102", inScope);
        Assert.Empty(inScopeDiags);

        var (outOfScope, outOfScopeDiags) = Expand(macros, "g(2)", callingFunctionId: "5");
        Assert.Equal("", outOfScope);
        Assert.Contains(outOfScopeDiags, d => d.Message.Contains("Unknown macro 'g'"));
    }

    [Fact]
    public void GlobalMacro_FallbackForAnyCaller()
    {
        var macros = new[] { Macro("g", "x + 1", "", ("x", FmiTypeDescriptor.Number)) };

        var (result, diagnostics) = Expand(macros, "g(2)", callingFunctionId: "3");

        Assert.Equal("3", result);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DepthLimit_EmitsError()
    {
        //m0 -> m1 -> ... -> m10: the 11th expansion exceeds MacroExpander.MaxDepth.
        var macros = new List<FmiMacroDefinition>();
        for (var i = 0; i < 11; i++)
        {
            var body = i < 10 ? $"m{i + 1}(x)" : "x";
            macros.Add(Macro($"m{i}", body, "", ("x", FmiTypeDescriptor.Number)));
        }

        var (result, diagnostics) = Expand(macros, "m0(1)");

        Assert.Equal("", result);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("maximum depth"));
    }

    [Fact]
    public void CircularMacro_EmitsError()
    {
        var macros = new[]
        {
            Macro("a", "b(1)"),
            Macro("b", "a(1)")
        };

        var (result, diagnostics) = Expand(macros, "a(1)");

        Assert.Equal("", result);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("circular reference"));
    }
}
