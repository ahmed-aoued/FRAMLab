using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
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
            var seen = new HashSet<string>(StringComparer.Ordinal); //case-sensitive (Name,FunctionId), matching legacy metadata keys
            foreach (XmlNode node in variablesRoot.ChildNodes)
            {
                if (node is XmlElement el && el.Name == "Variable")
                {
                    var def = FmiVariableDefinition.FromXml(el, definition.LoadDiagnostics);
                    if (def is not null)
                    {
                        if (!seen.Add(def.Name + "\n" + def.FunctionId))
                        {
                            definition.LoadDiagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error,
                                $"FMI/Variables/Variable[{def.Name}]",
                                $"Duplicate definition for name '{def.Name}' (function '{def.FunctionId}'); the first definition is kept."));
                            continue;
                        }
                        definition.Variables.Add(def);
                    }
                }
            }
            return definition;
        }

        /// <summary>
        /// Merges implicit definitions (derived from legacy metadata) into the declared
        /// definition. Declared variables win by name (a declared template also wins over
        /// function-bound implicits of the same name); implicits only fill gaps.
        /// Comparison is case-sensitive, matching legacy metadata keys.
        /// </summary>
        public FmiModelDefinition MergeWithImplicit(FmiModelDefinition declared, List<Function> functions)
        {
            var merged = new FmiModelDefinition();
            var declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in declared.Variables)
            {
                declaredNames.Add(def.Name);
                merged.Variables.Add(def);
            }
            foreach (var def in BuildImplicitDefinitions(functions).Variables)
            {
                if (declaredNames.Add(def.Name))
                {
                    merged.Variables.Add(def);
                }
            }
            foreach (var diagnostic in declared.LoadDiagnostics)
            {
                merged.LoadDiagnostics.Add(diagnostic);
            }
            return merged;
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
            var seen = new HashSet<string>(StringComparer.Ordinal); //case-sensitive (Name,FunctionId), matching legacy metadata keys
            foreach (var fn in functions)
            {
                if (fn.metadata is null) continue;
                foreach (var md in fn.metadata)
                {
                    var name = md.ElementAtOrDefault(0) ?? "";
                    if (name == "") continue;
                    //md[4] is the raw initial value; legacy rows are always 5 columns, guard short rows defensively
                    var initialRaw = md.ElementAtOrDefault(4) ?? "";
                    //Untyped inference only detects number/bool/string; collections are never guessed from commas
                    //(would change legacy equation semantics) — users opt in by declaring an explicit <Variable>.
                    var inferred = FmiValue.FromLegacyString(initialRaw);
                    var isGlobal = name.StartsWith("Global_");
                    var functionId = isGlobal ? "" : fn.IDNr;
                    if (!seen.Add(name + "\n" + functionId)) continue; //duplicate legacy key: keep first
                    definition.Variables.Add(new FmiVariableDefinition
                    {
                        Name = name,
                        Type = inferred.Type == FmiValueType.Number ? FmiTypeDescriptor.Number
                             : inferred.Type == FmiValueType.Boolean ? FmiTypeDescriptor.Boolean
                             : FmiTypeDescriptor.String,
                        Scope = isGlobal ? FmiVariableScope.Model : FmiVariableScope.Function,
                        InitialRaw = initialRaw,
                        InitialValue = inferred,
                        Persist = false,
                        FunctionId = functionId,
                        IsImplicit = true
                    });
                }
            }
            return definition;
        }
    }
}
