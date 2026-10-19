using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
{
    public class FmiDefinitionService : IFmiDefinitionService
    {
        public FmiModelDefinition Load(XmlDocument projectData)
        {
            var definition = new FmiModelDefinition();
            if (projectData.SelectSingleNode("//FM/FMI/Variables") is XmlNode variablesRoot)
            {
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
            }
            LoadMacros(projectData, definition);
            return definition;
        }

        private static void LoadMacros(XmlDocument projectData, FmiModelDefinition definition)
        {
            var macrosNode = projectData.SelectSingleNode("//FM/FMI/Macros");
            definition.MacrosNodeExistedAtLoad = macrosNode is not null;
            if (macrosNode is null)
            {
                return;
            }
            var seenNames = new HashSet<string>();
            foreach (XmlNode node in macrosNode.ChildNodes)
            {
                if (node is not XmlElement el || el.Name != "Macro")
                {
                    continue;
                }
                var def = FmiMacroDefinition.FromXml(el, definition.LoadDiagnostics);
                if (def is null)
                {
                    continue;
                }
                if (!seenNames.Add(def.Name))
                {
                    definition.LoadDiagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error,
                        $"FMI/Macros/Macro[{def.Name}]",
                        $"Duplicate macro name '{def.Name}'. Macro names must be unique across all scopes; this definition is rejected."));
                    continue;
                }
                foreach (var parameter in def.Parameters)
                {
                    if (definition.Variables.Any(v => v.Name == parameter.Name))
                    {
                        definition.LoadDiagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Info,
                            $"FMI/Macros/Macro[{def.Name}]",
                            $"Parameter '{parameter.Name}' has the same name as a declared variable; the parameter wins inside the macro body."));
                    }
                }
                definition.Macros.Add(def);
            }
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
            //<Macros> is created only by UpsertMacro so a legacy file that never had macros
            //stays byte-identical on save (see the backward-compatibility rule in the plan).
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

        public bool UpsertMacro(XmlDocument projectData, FmiMacroDefinition macro)
        {
            var root = EnsureFmiRoot(projectData);
            var macrosRoot = EnsureChild(projectData, root, "Macros");
            var el = macro.ToXml(projectData);
            if (FindMacro(macrosRoot, macro.Name, macro.FunctionId) is XmlElement existing)
            {
                macrosRoot.ReplaceChild(el, existing);
                return true;
            }
            //No shadowing: a macro name is globally unique, so a same-name macro in any
            //other scope rejects this upsert (the duplicate-name diagnostic surfaces at load).
            if (FindMacroByName(macrosRoot, macro.Name) is not null)
            {
                return false;
            }
            macrosRoot.AppendChild(el);
            return true;
        }

        public bool RemoveMacro(XmlDocument projectData, string name, string functionId)
        {
            if (projectData.SelectSingleNode("//FM/FMI/Macros") is XmlNode macrosRoot
                && FindMacro(macrosRoot, name, functionId) is XmlElement existing)
            {
                macrosRoot.RemoveChild(existing);
                return true;
            }
            return false;
        }

        private static XmlElement? FindMacro(XmlNode macrosRoot, string name, string functionId)
        {
            foreach (XmlNode node in macrosRoot.ChildNodes)
            {
                if (node is XmlElement el && el.Name == "Macro"
                    && el.GetAttribute("name") == name && el.GetAttribute("function") == functionId)
                {
                    return el;
                }
            }
            return null;
        }

        private static XmlElement? FindMacroByName(XmlNode macrosRoot, string name)
        {
            foreach (XmlNode node in macrosRoot.ChildNodes)
            {
                if (node is XmlElement el && el.Name == "Macro" && el.GetAttribute("name") == name)
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
