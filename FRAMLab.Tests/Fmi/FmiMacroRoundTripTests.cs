using System.Xml;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;
using Xunit;

namespace FRAMLab.Tests.Fmi;

/// <summary>
/// Backward-compatibility guarantee: legacy .xfmv files without a &lt;Macros&gt; node load
/// and save byte-identically, macro blocks round-trip, and macro edits target the
/// &lt;FMI&gt;&lt;Macros&gt; node without touching unrelated markup.
/// </summary>
public class FmiMacroRoundTripTests
{
    private static string LegacyFile => Path.Combine(AppContext.BaseDirectory, "TestFiles", "FMV_FeatureTest.xfmv");

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

    [Fact]
    public void LegacyFile_WithoutMacros_RoundTripsByteIdentical()
    {
        var original = File.ReadAllText(LegacyFile);
        var doc = new XmlDocument();
        doc.Load(LegacyFile);

        var definition = new FmiDefinitionService().Load(doc);

        Assert.False(definition.MacrosNodeExistedAtLoad);
        Assert.Empty(definition.Macros);
        Assert.Equal(original, doc.OuterXml);
    }

    [Fact]
    public void MacroFile_LoadsAndRoundTripsByteIdentical()
    {
        const string xml =
            "<FM Version=\"1\"><Functions></Functions>" +
            "<FMI version=\"1\"><Variables></Variables><Rules></Rules>" +
            "<Macros>" +
            "<Macro name=\"clamp01\" kind=\"formula\" return=\"number\" description=\"Clamp\">" +
            "<Param name=\"x\" type=\"number\" description=\"value\" />" +
            "<Body>min(max(x, 0), 1)</Body></Macro>" +
            "<Macro name=\"setFlag\" kind=\"rule\" function=\"3\"><Body>SET Flag = true</Body></Macro>" +
            "</Macros></FMI></FM>";

        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var definition = new FmiDefinitionService().Load(doc);

        Assert.True(definition.MacrosNodeExistedAtLoad);
        Assert.Equal(2, definition.Macros.Count);
        Assert.Equal("clamp01", definition.Macros[0].Name);
        Assert.Equal(FmiMacroKind.Formula, definition.Macros[0].Kind);
        Assert.Equal("setFlag", definition.Macros[1].Name);
        Assert.Equal(FmiMacroKind.Rule, definition.Macros[1].Kind);
        Assert.Equal("3", definition.Macros[1].FunctionId);
        Assert.Empty(definition.LoadDiagnostics);
        Assert.Equal(xml, doc.OuterXml);
    }

    [Fact]
    public void UpsertMacro_CreatesMacrosNode_InsideFmi()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM Version=\"1\"></FM>");
        var service = new FmiDefinitionService();

        Assert.True(service.UpsertMacro(doc, Macro("g", "1")));

        var macroNode = doc.SelectSingleNode("//FM/FMI/Macros/Macro");
        Assert.NotNull(macroNode);
        Assert.Equal("g", ((XmlElement)macroNode!).GetAttribute("name"));
        //EnsureFmiRoot also guarantees the sibling nodes so edits are well-formed.
        Assert.NotNull(doc.SelectSingleNode("//FM/FMI/Variables"));
        Assert.NotNull(doc.SelectSingleNode("//FM/FMI/Rules"));
    }

    [Fact]
    public void UpsertMacro_DuplicateNameAcrossScope_RejectedAtService()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM Version=\"1\"><FMI version=\"1\"><Variables></Variables><Rules></Rules></FMI></FM>");
        var service = new FmiDefinitionService();
        Assert.True(service.UpsertMacro(doc, Macro("g", "1")));

        Assert.False(service.UpsertMacro(doc, Macro("g", "2", "3")));

        Assert.Single(doc.SelectNodes("//FM/FMI/Macros/Macro")!.Cast<XmlNode>());
    }

    [Fact]
    public void RemoveMacro_RemovesNode()
    {
        var doc = new XmlDocument();
        doc.LoadXml("<FM Version=\"1\"><FMI version=\"1\"><Variables></Variables><Rules></Rules></FMI></FM>");
        var service = new FmiDefinitionService();
        service.UpsertMacro(doc, Macro("g", "1"));

        Assert.True(service.RemoveMacro(doc, "g", ""));
        Assert.Null(doc.SelectSingleNode("//FM/FMI/Macros/Macro"));
        Assert.False(service.RemoveMacro(doc, "g", ""));
    }
}
