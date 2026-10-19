using System.Xml;
using Xunit;
using FRAMLab.Shared;
using FRAMLab.Shared.MainApp.Services;

namespace FRAMLab.Tests.Fmi
{
    public class FmiDefinitionServiceTests
    {
        private readonly FmiDefinitionService service = new();

        private static XmlDocument LoadTestFile(string name)
        {
            var doc = new XmlDocument();
            doc.Load(Path.Combine(AppContext.BaseDirectory, "TestFiles", name));
            return doc;
        }

        [Fact]
        public void Load_DocumentWithoutFmiRoot_ReturnsEmptyDefinition_NoDiagnostics()
        {
            //Backward-compatibility pin: old .xfmv files keep loading
            var doc = LoadTestFile("FMV_FeatureTest.xfmv");
            var definition = service.Load(doc);
            Assert.Empty(definition.Variables);
            Assert.Empty(definition.LoadDiagnostics);
        }

        [Fact]
        public void NoUpsert_NoPhantomFmiNode_OnResave()
        {
            var doc = LoadTestFile("FMI_FRAM_Literature_Showcase.xfmv");
            service.Load(doc); //loading alone must not mutate the document
            Assert.Null(doc.SelectSingleNode("//FM/FMI"));
        }

        [Fact]
        public void UpsertVariable_CreatesFmiRoot_WithVariablesRulesMacros()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<FM Version=\"0,3,0,0\"><Functions/></FM>");
            service.UpsertVariable(doc, new FmiVariableDefinition
            {
                Name = "Risk_Score",
                Type = FmiTypeDescriptor.Number,
                Scope = FmiVariableScope.Cycle,
                InitialRaw = "0",
                InitialValue = FmiValue.Number(0)
            });

            var root = doc.SelectSingleNode("//FM/FMI");
            Assert.NotNull(root);
            Assert.Equal("1", ((XmlElement)root!).GetAttribute("version"));
            Assert.NotNull(root.SelectSingleNode("Variables"));
            Assert.NotNull(root.SelectSingleNode("Rules"));
            Assert.NotNull(root.SelectSingleNode("Macros"));

            var variable = root.SelectSingleNode("Variables/Variable") as XmlElement;
            Assert.NotNull(variable);
            Assert.Equal("Risk_Score", variable!.GetAttribute("name"));
            Assert.Equal("number", variable.GetAttribute("type"));
            Assert.Equal("cycle", variable.GetAttribute("scope"));
        }

        [Fact]
        public void UpsertVariable_ReplacesExisting_ByNameAndFunction()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<FM Version=\"0,3,0,0\"><Functions/></FM>");
            var first = new FmiVariableDefinition
            {
                Name = "Risk_Score",
                Type = FmiTypeDescriptor.Number,
                Scope = FmiVariableScope.Model,
                InitialRaw = "1",
                InitialValue = FmiValue.Number(1)
            };
            service.UpsertVariable(doc, first);
            var second = new FmiVariableDefinition
            {
                Name = "Risk_Score",
                Type = FmiTypeDescriptor.Number,
                Scope = FmiVariableScope.Model,
                InitialRaw = "2",
                InitialValue = FmiValue.Number(2)
            };
            service.UpsertVariable(doc, second);

            var variables = doc.SelectNodes("//FM/FMI/Variables/Variable")!;
            Assert.Single(variables);
            Assert.Equal("2", ((XmlElement)variables[0]!).GetAttribute("initial"));
        }

        [Fact]
        public void BuildImplicitDefinitions_FromLegacyMetadata()
        {
            var doc = LoadTestFile("FMV_FeatureTest.xfmv");
            var functions = new List<Function>();
            foreach (XmlElement fn in doc.SelectNodes("//FM/Functions/Function")!)
            {
                functions.Add(new Function(fn));
            }

            var definition = service.BuildImplicitDefinitions(functions);

            Assert.NotEmpty(definition.Variables);
            Assert.All(definition.Variables, v => Assert.True(v.IsImplicit));
            Assert.All(definition.Variables, v => Assert.False(v.Persist));

            var risk = definition.Variables.FirstOrDefault(v => v.Name == "Risk_Score");
            Assert.NotNull(risk);
            Assert.Equal(FmiVariableScope.Function, risk!.Scope);
            Assert.Equal("1", risk.FunctionId); //declared on function 1
            Assert.Equal(FmiValueType.Number, risk.Type.Kind); //"0" infers as number

            //no Global_* keys in this file: everything is function scope
            Assert.DoesNotContain(definition.Variables, v => v.Scope == FmiVariableScope.Model);
        }

        [Fact]
        public void BuildImplicitDefinitions_GlobalKeys_MapToModelScope()
        {
            var functions = new List<Function> { MakeFunction("7", ("Global_EscalationCount", "0"), ("Local", "abc")) };
            var definition = service.BuildImplicitDefinitions(functions);

            var global = definition.Variables.Single(v => v.Name == "Global_EscalationCount");
            Assert.Equal(FmiVariableScope.Model, global.Scope);
            Assert.Equal("", global.FunctionId);
            Assert.Equal(FmiValueType.Number, global.Type.Kind);

            var local = definition.Variables.Single(v => v.Name == "Local");
            Assert.Equal(FmiVariableScope.Function, local.Scope);
            Assert.Equal("7", local.FunctionId);
            Assert.Equal(FmiValueType.String, local.Type.Kind);
        }

        [Fact]
        public void BuildImplicitDefinitions_DuplicateKeys_KeepFirst()
        {
            var functions = new List<Function> { MakeFunction("7", ("Risk", "1"), ("Risk", "2")) };
            var definition = service.BuildImplicitDefinitions(functions);
            Assert.Single(definition.Variables);
            Assert.Equal("1", definition.Variables[0].InitialRaw);
        }

        [Fact]
        public void BuildImplicitDefinitions_ShortRows_AreTolerated()
        {
            var fn = MakeFunction("7", ("Risk", "1"));
            fn.metadata.Add(new[] { "Broken" }); //short row, no value column
            var definition = service.BuildImplicitDefinitions(new List<Function> { fn });
            Assert.Equal(2, definition.Variables.Count);
            Assert.Equal("", definition.Variables.Single(v => v.Name == "Broken").InitialRaw);
            Assert.Equal(FmiValueType.Empty, definition.Variables.Single(v => v.Name == "Broken").InitialValue.Type);
        }

        [Fact]
        public void MergeWithImplicit_DeclaredWins_ImplicitFillsGaps()
        {
            var declaredDoc = new XmlDocument();
            declaredDoc.LoadXml("<FM><FMI version=\"1\"><Variables>"
                + "<Variable name=\"Risk_Score\" type=\"number\" scope=\"model\" initial=\"5\"/>"
                + "</Variables></FMI></FM>");
            var declared = service.Load(declaredDoc);
            var functions = new List<Function> { MakeFunction("7", ("Risk_Score", "9"), ("Impact", "3")) };

            var merged = service.MergeWithImplicit(declared, functions);

            Assert.Equal(2, merged.Variables.Count);
            var risk = merged.Variables.Single(v => v.Name == "Risk_Score");
            Assert.False(risk.IsImplicit); //declared wins over implicit with same (Name, FunctionId="")
            Assert.Equal("5", risk.InitialRaw);
            Assert.True(merged.Variables.Single(v => v.Name == "Impact").IsImplicit); //gap filled
        }

        [Fact]
        public void MergeWithImplicit_ImplicitTemplateDropped_WhenDeclaredTemplateExists()
        {
            var declaredDoc = new XmlDocument();
            declaredDoc.LoadXml("<FM><FMI version=\"1\"><Variables>"
                + "<Variable name=\"Risk_Score\" type=\"number\" scope=\"function\" initial=\"0\"/>"
                + "</Variables></FMI></FM>");
            var declared = service.Load(declaredDoc);
            //implicit function-bound key produces the same (Name, FunctionId="") key as the declared template
            var functions = new List<Function> { MakeFunction("7", ("Risk_Score", "1")) };

            var merged = service.MergeWithImplicit(declared, functions);

            Assert.Single(merged.Variables);
            Assert.False(merged.Variables[0].IsImplicit);
        }

        [Fact]
        public void MergeWithImplicit_CaseSensitive_RiskAndRISKCoexist()
        {
            var declaredDoc = new XmlDocument();
            declaredDoc.LoadXml("<FM><FMI version=\"1\"><Variables>"
                + "<Variable name=\"Risk\" type=\"number\" scope=\"function\" function=\"7\" initial=\"0\"/>"
                + "</Variables></FMI></FM>");
            var declared = service.Load(declaredDoc);
            var functions = new List<Function> { MakeFunction("7", ("RISK", "1")) };

            var merged = service.MergeWithImplicit(declared, functions);

            Assert.Equal(2, merged.Variables.Count); //case-sensitive: no dedup between Risk and RISK
        }

        private static Function MakeFunction(string id, params (string Name, string Value)[] metadata)
        {
            var doc = new XmlDocument();
            var xml = $"<Function><IDNr>{id}</IDNr><metadata>"
                + string.Join("", metadata.Select(m => $"<{m.Name} active=\"true\" equation=\"\">{m.Value}</{m.Name}>"))
                + "</metadata></Function>";
            doc.LoadXml(xml);
            return new Function(doc.DocumentElement!);
        }
    }
}
