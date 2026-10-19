using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Cross-feature regression: the typed-variable branch introduced
/// <see cref="FmiDefinitionService.MergeWithImplicit"/>, which StateContainer now uses at
/// runtime. The macro branch adds macros to the declared definition. The merge must keep
/// both, otherwise macros are silently lost when the FMI runtime initialises.
/// </summary>
public class FmiDefinitionMergeTests
{
    private const string ModelXml =
        "<FM Version=\"1\"><Functions></Functions>" +
        "<FMI version=\"1\">" +
        "<Variables><Variable name=\"Count\" type=\"number\" scope=\"function\" function=\"1\" initial=\"0\" persist=\"true\" /></Variables>" +
        "<Rules></Rules>" +
        "<Macros><Macro name=\"doubleIt\" kind=\"formula\" return=\"number\"><Param name=\"x\" type=\"number\" /><Body>x * 2</Body></Macro></Macros>" +
        "</FMI></FM>";

    [Fact]
    public void MergeWithImplicit_KeepsDeclaredVariablesAndMacros()
    {
        var doc = new XmlDocument();
        doc.LoadXml(ModelXml);
        var service = new FmiDefinitionService();

        var declared = service.Load(doc);
        Assert.Single(declared.Variables);
        Assert.Single(declared.Macros);
        Assert.True(declared.MacrosNodeExistedAtLoad);

        var merged = service.MergeWithImplicit(declared, new List<Function>());

        Assert.Single(merged.Variables);
        Assert.Equal("Count", merged.Variables[0].Name);
        Assert.Single(merged.Macros);
        Assert.Equal("doubleIt", merged.Macros[0].Name);
        Assert.True(merged.MacrosNodeExistedAtLoad);
        Assert.Empty(merged.LoadDiagnostics);
    }

    [Fact]
    public void MergeWithImplicit_WithoutMacros_KeepsMacroListEmpty()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM Version=\"1\"><Functions></Functions><FMI version=\"1\"><Variables></Variables></FMI></FM>");
        var service = new FmiDefinitionService();

        var merged = service.MergeWithImplicit(service.Load(doc), new List<Function>());

        Assert.Empty(merged.Macros);
        Assert.False(merged.MacrosNodeExistedAtLoad);
    }
}
