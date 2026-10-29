using FRAMLab.Shared.MainApp.Services.Scripting;
using Xunit;

namespace FRAMLab.Tests.Scripting;

/// <summary>Autocomplete suggestions are built from the compiler's symbol table.</summary>
public class ScriptCompletionsTests
{
    [Fact]
    public void Suggests_DeclaredVariables_Aspects_BuiltIns_KeywordsAndSnippets()
    {
        var symbols = new SymbolTable();
        symbols.Add(new Symbol("Counter", "INT", true, "Static"));
        symbols.Add(new Symbol("Grid", "REAL", true, "Temp", isArray: true, rank: 2));
        symbols.Add(new Symbol("Fire_signal", "BOOL", false, "Input"));
        symbols.Add(new Symbol("Alarm", "ANY", true, "Output"));
        symbols.Add(new Symbol("Cycle", "INT", false, "Builtin"));

        var items = ScriptCompletions.Build(symbols).ToDictionary(i => i.Label);

        Assert.Equal("Static INT", items["Counter"].Detail);
        Assert.Equal("Grid[${1:row}, ${2:col}]", items["Grid"].InsertText);
        Assert.True(items["Grid"].IsSnippet);
        Assert.Equal("aspect", items["Fire_signal"].Kind);
        Assert.Contains("read-only", items["Fire_signal"].Detail);
        Assert.DoesNotContain("read-only", items["Alarm"].Detail);
        Assert.Equal("LIMIT(${1:min}, ${2:x}, ${3:max})", items["LIMIT"].InsertText);
        Assert.Equal("function", items["SetOutput"].Kind);
        Assert.Equal("keyword", items["END_CASE"].Kind);
        Assert.Contains("END_CASE;", items["CASE … END_CASE"].InsertText);
    }
}
