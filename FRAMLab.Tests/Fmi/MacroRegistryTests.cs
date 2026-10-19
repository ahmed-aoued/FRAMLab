using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Scope-aware registry contract: global fallback lookup, global name uniqueness (no
/// shadowing), replace-in-place upsert, removal, and static validation (unknown-callee
/// scan + call-graph cycle poisoning).
/// </summary>
public class MacroRegistryTests
{
    private static FmiMacroDefinition Macro(string name, string body, string functionId = "",
        FmiMacroKind kind = FmiMacroKind.Formula, params (string Name, FmiTypeDescriptor Type)[] parameters)
    {
        var macro = new FmiMacroDefinition { Name = name, Kind = kind, Body = body, FunctionId = functionId };
        foreach (var (pn, pt) in parameters)
        {
            macro.Parameters.Add(new FmiMacroParameter { Name = pn, Type = pt });
        }
        return macro;
    }

    private static FmiModelDefinition Definition(params FmiMacroDefinition[] macros)
    {
        var definition = new FmiModelDefinition();
        definition.Macros.AddRange(macros);
        return definition;
    }

    [Fact]
    public void LoadFrom_GlobalMacro_VisibleFromAnyFunction()
    {
        var registry = new MacroRegistry();
        registry.LoadFrom(Definition(Macro("g", "x + 1", "", FmiMacroKind.Formula, ("x", FmiTypeDescriptor.Number))));

        Assert.True(registry.TryGetMacro("g", "", out _));
        Assert.True(registry.TryGetMacro("g", "3", out _));
        Assert.True(registry.TryGetMacro("g", "99", out _));
        Assert.False(registry.TryGetMacro("missing", "3", out _));
    }

    [Fact]
    public void LoadFrom_FunctionScoped_OnlyVisibleInItsFunction()
    {
        var registry = new MacroRegistry();
        registry.LoadFrom(Definition(Macro("g", "x + 100", "3", FmiMacroKind.Formula, ("x", FmiTypeDescriptor.Number))));

        Assert.True(registry.TryGetMacro("g", "3", out _));
        Assert.False(registry.TryGetMacro("g", "4", out _));
        Assert.False(registry.TryGetMacro("g", "", out _));
    }

    [Fact]
    public void LoadFrom_DuplicateNameAcrossScopes_RejectedFirstWins()
    {
        var definition = Definition(
            Macro("g", "1", "", FmiMacroKind.Formula),
            Macro("g", "2", "3", FmiMacroKind.Formula));
        var registry = new MacroRegistry();

        registry.LoadFrom(definition);

        var macro = Assert.Single(registry.All);
        Assert.Equal("1", macro.Body);
        Assert.Contains(definition.LoadDiagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("unique"));
    }

    [Fact]
    public void Upsert_CrossScopeDuplicate_Rejected()
    {
        var registry = new MacroRegistry();

        Assert.True(registry.Upsert(Macro("g", "1", "")));
        Assert.False(registry.Upsert(Macro("g", "2", "3")));

        Assert.Single(registry.All);
        Assert.Contains(registry.Diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("another scope"));
    }

    [Fact]
    public void Upsert_ReplaceInPlace_UpdatesBody()
    {
        var registry = new MacroRegistry();
        registry.Upsert(Macro("g", "1", ""));

        Assert.True(registry.Upsert(Macro("g", "2", "")));

        var macro = Assert.Single(registry.All);
        Assert.Equal("2", macro.Body);
    }

    [Fact]
    public void Remove_RemovesAndReports()
    {
        var registry = new MacroRegistry();
        registry.Upsert(Macro("g", "1", ""));

        Assert.True(registry.Remove("g", ""));
        Assert.Empty(registry.All);
        Assert.False(registry.Remove("g", ""));
    }

    [Fact]
    public void Cycle_TwoMacros_Poisoned()
    {
        var a = Macro("a", "b(1)");
        var b = Macro("b", "a(1)");
        var definition = Definition(a, b);
        var registry = new MacroRegistry();

        registry.LoadFrom(definition);

        Assert.True(registry.IsPoisoned(a));
        Assert.True(registry.IsPoisoned(b));
        Assert.Contains(registry.Diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("circular"));
        Assert.Contains(definition.LoadDiagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("circular"));
    }

    [Fact]
    public void Cycle_SelfReference_Poisoned()
    {
        var a = Macro("a", "a(1)");
        var registry = new MacroRegistry();

        registry.LoadFrom(Definition(a));

        Assert.True(registry.IsPoisoned(a));
        Assert.Contains(registry.Diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("circular"));
    }

    [Fact]
    public void UnknownCallee_EmitsDiagnostic()
    {
        var registry = new MacroRegistry();

        registry.LoadFrom(Definition(Macro("a", "nope(1)")));

        Assert.Contains(registry.Diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("unknown macro 'nope'"));
    }

    [Fact]
    public void NotVisibleCallee_EmitsDiagnostic()
    {
        var registry = new MacroRegistry();
        var definition = Definition(
            Macro("a", "hidden(1)"),
            Macro("hidden", "1", "3"));

        registry.LoadFrom(definition);

        Assert.Contains(registry.Diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("not visible"));
    }

    [Fact]
    public void GetSymbols_FiltersByScope()
    {
        var registry = new MacroRegistry();
        registry.LoadFrom(Definition(
            Macro("g", "1"),
            Macro("h", "1", "3"),
            Macro("i", "1", "4")));

        Assert.Equal(new[] { "g" }, registry.GetSymbols("").Select(s => s.Name));
        Assert.Equal(new[] { "g", "h" }, registry.GetSymbols("3").Select(s => s.Name));
    }
}
