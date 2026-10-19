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
            }
            LoadMacros(projectData, definition);
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
            // Macros are part of the declared model and must survive the implicit-definition
            // merge; otherwise StateContainer.initFmiRuntime would silently drop them.
            foreach (var macro in declared.Macros)
            {
                merged.Macros.Add(macro);
            }
            merged.MacrosNodeExistedAtLoad = declared.MacrosNodeExistedAtLoad;
            return merged;
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
