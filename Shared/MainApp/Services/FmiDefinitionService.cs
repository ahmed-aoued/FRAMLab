using System.Xml;

namespace FMV_OG.Shared.MainApp.Services
{
    public class FmiDefinitionService : IFmiDefinitionService
    {
        public FmiModelDefinition Load(XmlDocument projectData)
        {
            var definition = new FmiModelDefinition();
            if (projectData.SelectSingleNode("//FM/FMI/Variables") is not XmlNode variablesRoot)
            {
                return definition; //old files without <FMI> load to an empty definition
            }
            foreach (XmlNode node in variablesRoot.ChildNodes)
            {
                if (node is XmlElement el && el.Name == "Variable")
                {
                    var def = FmiVariableDefinition.FromXml(el, definition.LoadDiagnostics);
                    if (def is not null)
                    {
                        definition.Variables.Add(def);
                    }
                }
            }
            return definition;
        }

        public XmlNode EnsureFmiRoot(XmlDocument projectData)
        {
            if (projectData.SelectSingleNode("//FM/FMI") is not XmlNode root)
            {
                root = projectData.CreateElement("FMI");
                ((XmlElement)root).SetAttribute("version", "1");
                projectData.SelectSingleNode("//FM")!.AppendChild(root);
            }
            EnsureChild(projectData, root, "Variables");
            EnsureChild(projectData, root, "Rules");
            EnsureChild(projectData, root, "Macros");
            return root;
        }

        private static XmlNode EnsureChild(XmlDocument projectData, XmlNode parent, string name)
        {
            if (parent.SelectSingleNode(name) is not XmlNode child)
            {
                child = projectData.CreateElement(name);
                parent.AppendChild(child);
            }
            return child;
        }

        public void UpsertVariable(XmlDocument projectData, FmiVariableDefinition def)
        {
            var root = EnsureFmiRoot(projectData);
            var variablesRoot = root.SelectSingleNode("Variables")!;
            var existing = FindVariable(variablesRoot, def.Name, def.FunctionId);
            var el = def.ToXml(projectData);
            if (existing is not null)
            {
                variablesRoot.ReplaceChild(el, existing);
            }
            else
            {
                variablesRoot.AppendChild(el);
            }
        }

        public void RemoveVariable(XmlDocument projectData, string name, string functionId)
        {
            if (projectData.SelectSingleNode("//FM/FMI/Variables") is XmlNode variablesRoot
                && FindVariable(variablesRoot, name, functionId) is XmlElement existing)
            {
                variablesRoot.RemoveChild(existing);
            }
        }

        private static XmlElement? FindVariable(XmlNode variablesRoot, string name, string functionId)
        {
            foreach (XmlNode node in variablesRoot.ChildNodes)
            {
                if (node is XmlElement el && el.Name == "Variable"
                    && el.GetAttribute("name") == name && el.GetAttribute("function") == functionId)
                {
                    return el;
                }
            }
            return null;
        }

        public FmiModelDefinition BuildImplicitDefinitions(List<Function> functions)
        {
            var definition = new FmiModelDefinition();
            foreach (var fn in functions)
            {
                if (fn.metadata is null) continue;
                foreach (var md in fn.metadata)
                {
                    var inferred = FmiValue.FromLegacyString(md[4]);
                    definition.Variables.Add(new FmiVariableDefinition
                    {
                        Name = md[0],
                        Type = inferred.Type == FmiValueType.Number ? FmiTypeDescriptor.Number
                             : inferred.Type == FmiValueType.Boolean ? FmiTypeDescriptor.Boolean
                             : FmiTypeDescriptor.String,
                        Scope = md[0].StartsWith("Global_") ? FmiVariableScope.Model : FmiVariableScope.Function,
                        InitialRaw = md[4],
                        InitialValue = inferred,
                        Persist = false,
                        FunctionId = md[0].StartsWith("Global_") ? "" : fn.IDNr,
                        IsImplicit = true
                    });
                }
            }
            return definition;
        }
    }
}
