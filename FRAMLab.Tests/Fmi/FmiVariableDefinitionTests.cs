using System.Xml;
using Xunit;
using FRAMLab.Shared;

namespace FRAMLab.Tests.Fmi
{
    public class FmiVariableDefinitionTests
    {
        private static XmlElement VariableElement(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml($"<Variables>{xml}</Variables>");
            return (XmlElement)doc.DocumentElement!.FirstChild!;
        }

        private static (FmiVariableDefinition? Def, List<FmiDiagnostic> Diagnostics) Parse(string xml)
        {
            var diagnostics = new List<FmiDiagnostic>();
            var def = FmiVariableDefinition.FromXml(VariableElement(xml), diagnostics);
            return (def, diagnostics);
        }

        [Theory]
        [InlineData("cycle")]
        [InlineData("function")]
        [InlineData("model")]
        [InlineData("input")] //parse-only scope (Feature 2 runtime semantics)
        [InlineData("output")] //parse-only scope (Feature 2 runtime semantics)
        public void FromXml_ToXml_RoundTrips_EveryScope(string scope)
        {
            var xml = $"<Variable name=\"Risk_Score\" type=\"number\" scope=\"{scope}\" initial=\"0\" description=\"d\"/>";
            var (def, diagnostics) = Parse(xml);
            Assert.NotNull(def);
            Assert.Empty(diagnostics);

            var doc = new XmlDocument();
            var el = def!.ToXml(doc);
            Assert.Equal("Risk_Score", el.GetAttribute("name"));
            Assert.Equal("number", el.GetAttribute("type"));
            Assert.Equal(scope, el.GetAttribute("scope"));
            Assert.Equal("0", el.GetAttribute("initial"));
            Assert.Equal("d", el.GetAttribute("description"));
        }

        [Fact]
        public void FromXml_ToXml_RoundTrips_FullAttributes()
        {
            var xml = "<Variable name=\"Risk_Score\" type=\"number\" scope=\"function\" function=\"3\" initial=\"0.5\" persist=\"true\" description=\"per-fn override\"/>";
            var (def, diagnostics) = Parse(xml);
            Assert.NotNull(def);
            Assert.Empty(diagnostics);

            var doc = new XmlDocument();
            var el = def!.ToXml(doc);
            Assert.Equal("3", el.GetAttribute("function"));
            Assert.Equal("0.5", el.GetAttribute("initial"));
            Assert.Equal("true", el.GetAttribute("persist"));
            Assert.Equal("per-fn override", el.GetAttribute("description"));
        }

        [Fact]
        public void ToXml_OmitsDefaultAttributes()
        {
            var (def, _) = Parse("<Variable name=\"Risk_Score\" type=\"number\" scope=\"cycle\" initial=\"0\"/>");
            var doc = new XmlDocument();
            var el = def!.ToXml(doc);
            Assert.False(el.HasAttribute("persist")); //false = default, not emitted
            Assert.False(el.HasAttribute("function")); //template = default, not emitted
            Assert.False(el.HasAttribute("description"));
        }

        [Fact]
        public void FromXml_BadName_IsError()
        {
            var (def, diagnostics) = Parse("<Variable name=\"risk\" type=\"number\" scope=\"model\"/>");
            Assert.Null(def);
            Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error && d.Source == "FMI/Variables/Variable[risk]");
        }

        [Fact]
        public void FromXml_BadType_IsError()
        {
            var (def, diagnostics) = Parse("<Variable name=\"Risk\" type=\"table\" scope=\"model\"/>");
            Assert.Null(def);
            Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error);
        }

        [Fact]
        public void FromXml_BadScope_IsError()
        {
            var (def, diagnostics) = Parse("<Variable name=\"Risk\" type=\"number\" scope=\"global\"/>");
            Assert.Null(def);
            Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Error);
        }

        [Theory]
        [InlineData("cycle")]
        [InlineData("input")]
        [InlineData("output")]
        public void FromXml_PersistOnNonPersistentScope_WarnsAndForcesFalse(string scope)
        {
            var (def, diagnostics) = Parse($"<Variable name=\"Risk\" type=\"number\" scope=\"{scope}\" persist=\"true\"/>");
            Assert.NotNull(def);
            Assert.False(def!.Persist);
            Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Warning);
        }

        [Fact]
        public void FromXml_FunctionOnModelScope_WarnsAndDropsAttribute()
        {
            var (def, diagnostics) = Parse("<Variable name=\"Risk\" type=\"number\" scope=\"model\" function=\"3\"/>");
            Assert.NotNull(def);
            Assert.Equal("", def!.FunctionId);
            Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Warning);

            var doc = new XmlDocument();
            Assert.False(def.ToXml(doc).HasAttribute("function"));
        }

        [Fact]
        public void FromXml_InitialCoercionFailure_Warns_RawPreservedOnReserialize()
        {
            var (def, diagnostics) = Parse("<Variable name=\"Risk\" type=\"number\" scope=\"model\" initial=\"abc\"/>");
            Assert.NotNull(def);
            Assert.Contains(diagnostics, d => d.Severity == FmiDiagnosticSeverity.Warning);
            Assert.Equal("abc", def!.InitialRaw);

            //D2: bad raw initial is written back verbatim (no data loss)
            var doc = new XmlDocument();
            Assert.Equal("abc", def.ToXml(doc).GetAttribute("initial"));
        }

        [Fact]
        public void CaseSensitivity_Risk_And_RISK_AreDistinct()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<FM><FMI version=\"1\"><Variables>"
                + "<Variable name=\"Risk\" type=\"number\" scope=\"model\"/>"
                + "<Variable name=\"RISK\" type=\"string\" scope=\"model\"/>"
                + "</Variables></FMI></FM>");
            var service = new FRAMLab.Shared.MainApp.Services.FmiDefinitionService();
            var definition = service.Load(doc);
            Assert.Equal(2, definition.Variables.Count);
            Assert.DoesNotContain(definition.LoadDiagnostics, d => d.Severity == FmiDiagnosticSeverity.Error);
        }

        [Fact]
        public void Duplicate_NameAndFunction_IsError_FirstKept()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<FM><FMI version=\"1\"><Variables>"
                + "<Variable name=\"Risk\" type=\"number\" scope=\"model\" initial=\"1\"/>"
                + "<Variable name=\"Risk\" type=\"string\" scope=\"model\" initial=\"x\"/>"
                + "</Variables></FMI></FM>");
            var service = new FRAMLab.Shared.MainApp.Services.FmiDefinitionService();
            var definition = service.Load(doc);
            Assert.Single(definition.Variables);
            Assert.Equal(FmiValueType.Number, definition.Variables[0].Type.Kind); //first kept
            Assert.Contains(definition.LoadDiagnostics, d => d.Severity == FmiDiagnosticSeverity.Error);
        }

        [Fact]
        public void TemplatePlusOverride_SameName_IsLegal_NoDiagnostic()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<FM><FMI version=\"1\"><Variables>"
                + "<Variable name=\"Risk_Score\" type=\"number\" scope=\"cycle\" initial=\"0\"/>"
                + "<Variable name=\"Risk_Score\" type=\"number\" scope=\"function\" function=\"3\" initial=\"0.5\"/>"
                + "</Variables></FMI></FM>");
            var service = new FRAMLab.Shared.MainApp.Services.FmiDefinitionService();
            var definition = service.Load(doc);
            Assert.Equal(2, definition.Variables.Count);
            Assert.DoesNotContain(definition.LoadDiagnostics, d => d.Severity == FmiDiagnosticSeverity.Error);
        }
    }
}
