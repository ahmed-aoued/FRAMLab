using System.Xml;
using FRAMLab.Shared;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// XML parse/serialize contract for &lt;FMI&gt;&lt;Macros&gt;&lt;Macro&gt; elements:
/// attribute defaults, name validation, return-type handling, and body rules.
/// </summary>
public class FmiMacroDefinitionXmlTests
{
    private static XmlElement MacroElement(string innerXml)
    {
        var doc = new XmlDocument();
        doc.LoadXml($"<root>{innerXml}</root>");
        return (XmlElement)doc.DocumentElement!.FirstChild!;
    }

    [Fact]
    public void FromXml_ParsesFormulaMacro()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement(
            "<Macro name=\"clamp01\" kind=\"formula\" return=\"number\" function=\"3\" description=\"Clamp\">" +
            "<Param name=\"x\" type=\"number\" description=\"value\" />" +
            "<Body>min(max(x, 0), 1)</Body></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.NotNull(macro);
        Assert.Equal("clamp01", macro!.Name);
        Assert.Equal(FmiValueType.Number, macro.ReturnType!.Kind);
        Assert.Equal("3", macro.FunctionId);
        Assert.Equal("Clamp", macro.Description);
        var param = Assert.Single(macro.Parameters);
        Assert.Equal("x", param.Name);
        Assert.Equal(FmiValueType.Number, param.Type.Kind);
        Assert.Equal("value", param.Description);
        Assert.Equal("min(max(x, 0), 1)", macro.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void FromXml_AcceptsMissingOrFormulaKind()
    {
        var diagnostics = new List<FmiDiagnostic>();

        Assert.NotNull(FmiMacroDefinition.FromXml(MacroElement("<Macro name=\"f\"><Body>1</Body></Macro>"), diagnostics));
        Assert.NotNull(FmiMacroDefinition.FromXml(MacroElement("<Macro name=\"g\" kind=\"formula\"><Body>1</Body></Macro>"), diagnostics));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void FromXml_RejectsInvalidMacroName()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement("<Macro name=\"Clamp\"><Body>1</Body></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.Null(macro);
        var error = Assert.Single(diagnostics);
        Assert.Equal(FmiDiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Macro name", error.Message);
    }

    [Fact]
    public void FromXml_RejectsInvalidParameterName()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement("<Macro name=\"f\"><Param name=\"1x\" type=\"number\" /><Body>1</Body></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.Null(macro);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("Parameter name"));
    }

    [Fact]
    public void FromXml_RejectsUnknownKind()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement("<Macro name=\"f\" kind=\"bogus\"><Body>1</Body></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.Null(macro);
        Assert.Contains(diagnostics, d => d.Message.Contains("Unknown kind"));
    }

    [Fact]
    public void FromXml_RejectsInvalidReturnType()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement("<Macro name=\"f\" return=\"bogus\"><Body>1</Body></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.Null(macro);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error);
    }

    [Fact]
    public void FromXml_RejectsRuleMacros_WithGuidance()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement("<Macro name=\"r\" kind=\"rule\"><Body>SET X = 1</Body></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.Null(macro);
        var error = Assert.Single(diagnostics);
        Assert.Equal(FmiDiagnosticSeverity.Error, error.Severity);
        Assert.Contains("ST script", error.Message);
    }

    [Fact]
    public void FromXml_EmptyFormulaBody_Errors()
    {
        var diagnostics = new List<FmiDiagnostic>();
        var el = MacroElement("<Macro name=\"f\"></Macro>");

        var macro = FmiMacroDefinition.FromXml(el, diagnostics);

        Assert.Null(macro);
        Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Message.Contains("must not be empty"));
    }

    [Fact]
    public void ToXml_WritesAllAttributes()
    {
        var macro = new FmiMacroDefinition
        {
            Name = "clamp01",
            Body = "min(max(x, 0), 1)",
            ReturnType = FmiTypeDescriptor.Number,
            FunctionId = "3",
            Description = "Clamp"
        };
        macro.Parameters.Add(new FmiMacroParameter { Name = "x", Type = FmiTypeDescriptor.Number, Description = "value" });

        var el = macro.ToXml(new XmlDocument());

        Assert.Equal("clamp01", el.GetAttribute("name"));
        Assert.False(el.HasAttribute("kind"));
        Assert.Equal("number", el.GetAttribute("return"));
        Assert.Equal("3", el.GetAttribute("function"));
        Assert.Equal("Clamp", el.GetAttribute("description"));
        var param = Assert.IsType<XmlElement>(el.SelectSingleNode("Param"));
        Assert.Equal("x", param.GetAttribute("name"));
        Assert.Equal("number", param.GetAttribute("type"));
        Assert.Equal("value", param.GetAttribute("description"));
        Assert.Equal("min(max(x, 0), 1)", el.SelectSingleNode("Body")!.InnerText);
    }

    [Fact]
    public void ToXml_OmitsOptionalAttributes()
    {
        var macro = new FmiMacroDefinition { Name = "f", Body = "1" };

        var el = macro.ToXml(new XmlDocument());

        Assert.Equal("f", el.GetAttribute("name"));
        Assert.False(el.HasAttribute("return"));
        Assert.False(el.HasAttribute("function"));
        Assert.False(el.HasAttribute("description"));
        Assert.Equal("1", el.SelectSingleNode("Body")!.InnerText);
    }
}
