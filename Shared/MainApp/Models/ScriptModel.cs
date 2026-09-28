using System.Xml;

namespace FMV_OG.Shared.MainApp.Models
{
    /// <summary>
    /// The ST script attached to one FRAM function: source text + declared variables.
    /// Persisted inside the function XML node as
    /// &lt;script enabled="true" language="ST" version="1"&gt;CDATA&lt;/script&gt; and
    /// &lt;variables&gt;&lt;var name=".." scope=".." type=".." init=".." output=".."/&gt;...&lt;/variables&gt;.
    /// </summary>
    public class ScriptModel
    {
        public bool Enabled { get; set; } = true;
        public string Language { get; set; } = "ST";
        public string Source { get; set; } = "";
        public List<ScriptVariable> Variables { get; set; } = new List<ScriptVariable>();
        /// <summary>
        /// Aspect variables derived from the model's couplings (Input/Output/Precondition/
        /// Resource/Control/Time). Only their user-assigned data types are persisted; the
        /// set of aspects itself is always re-derived from couplings via SyncAspects.
        /// </summary>
        public List<AspectVariable> AspectVariables { get; set; } = new List<AspectVariable>();

        public bool HasContent =>
            !string.IsNullOrWhiteSpace(Source)
            || Variables.Count > 0
            || AspectVariables.Any(a => a.Type is not null);

        /// <summary>
        /// Re-derive the aspect set from couplings and merge with the persisted type
        /// assignments, keeping any types the user already set for aspects that still exist.
        /// </summary>
        public void SyncAspects(IEnumerable<Coupling> couplings, string functionIdNr)
        {
            var derived = AspectVariable.DeriveFor(functionIdNr, couplings);
            foreach (var d in derived)
            {
                var existing = AspectVariables.FirstOrDefault(a => a.Category == d.Category && a.Label == d.Label);
                if (existing is not null)
                {
                    d.Type = existing.Type; // preserve user-assigned type
                }
            }
            AspectVariables = derived;
        }

        /// <summary>Persisted type assignments read from the XML (aspect label+category -> type).</summary>
        private void ReadAspectTypes(XmlNode fn)
        {
            if (fn.SelectSingleNode("aspectTypes") is XmlNode atNode)
            {
                foreach (XmlNode a in atNode.SelectNodes("aspect")!)
                {
                    var cat = a.SelectSingleNode("@category")?.Value ?? "";
                    var label = a.SelectSingleNode("@label")?.Value ?? "";
                    var typeStr = a.SelectSingleNode("@type")?.Value ?? "";
                    if (label == "" || !Enum.TryParse<AspectCategory>(cat, true, out var category)) continue;
                    if (!Enum.TryParse<ScriptVarType>(typeStr, true, out var type)) continue;
                    var existing = AspectVariables.FirstOrDefault(x => x.Category == category && x.Label == label);
                    if (existing is not null)
                    {
                        existing.Type = type;
                    }
                    else
                    {
                        AspectVariables.Add(new AspectVariable { Category = category, Label = label, Type = type });
                    }
                }
            }
        }

        /// <summary>Parse the &lt;script&gt; and &lt;variables&gt; children of a function XML node. Never returns null.</summary>
        public static ScriptModel FromXml(XmlNode fn)
        {
            var model = new ScriptModel();

            if (fn.SelectSingleNode("script") is XmlNode scriptNode)
            {
                model.Enabled = scriptNode.SelectSingleNode("@enabled")?.Value != "false";
                model.Language = scriptNode.SelectSingleNode("@language")?.Value ?? "ST";
                model.Source = scriptNode.InnerText;
            }

            if (fn.SelectSingleNode("variables") is XmlNode varsNode)
            {
                foreach (XmlNode varNode in varsNode.SelectNodes("var")!)
                {
                    var v = new ScriptVariable
                    {
                        Name = varNode.SelectSingleNode("@name")?.Value ?? "",
                        InitialValue = varNode.SelectSingleNode("@init")?.Value ?? "",
                        IsOutput = varNode.SelectSingleNode("@output")?.Value == "true"
                    };
                    if (Enum.TryParse<ScriptScope>(varNode.SelectSingleNode("@scope")?.Value ?? "Temp", true, out var scope))
                    {
                        v.Scope = scope;
                    }
                    if (Enum.TryParse<ScriptVarType>(varNode.SelectSingleNode("@type")?.Value ?? "REAL", true, out var type))
                    {
                        v.Type = type;
                    }
                    if (v.Name != "")
                    {
                        model.Variables.Add(v);
                    }
                }
            }

            model.ReadAspectTypes(fn);
            return model;
        }

        /// <summary>
        /// Write this script back into the function XML node. Removes the nodes entirely
        /// when there is no content, keeping saved .xfmv files lean.
        /// </summary>
        public void WriteXml(XmlDocument doc, XmlNode fn)
        {
            var existingScript = fn.SelectSingleNode("script");
            var existingVars = fn.SelectSingleNode("variables");
            var existingAspectTypes = fn.SelectSingleNode("aspectTypes");

            if (!HasContent)
            {
                if (existingScript is not null) fn.RemoveChild(existingScript);
                if (existingVars is not null) fn.RemoveChild(existingVars);
                if (existingAspectTypes is not null) fn.RemoveChild(existingAspectTypes);
                return;
            }

            // Only materialise the <script> node when there is actual source text,
            // so a function with just aspect types doesn't get an empty script element.
            if (!string.IsNullOrWhiteSpace(Source))
            {
                XmlNode scriptNode;
                if (existingScript is null)
                {
                    scriptNode = doc.CreateElement("script");
                    fn.AppendChild(scriptNode);
                }
                else
                {
                    scriptNode = existingScript;
                    scriptNode.RemoveAll();
                }

                var enabledAttr = doc.CreateAttribute("enabled");
                enabledAttr.Value = Enabled ? "true" : "false";
                scriptNode.Attributes!.Append(enabledAttr);
                var langAttr = doc.CreateAttribute("language");
                langAttr.Value = Language;
                scriptNode.Attributes.Append(langAttr);
                var versionAttr = doc.CreateAttribute("version");
                versionAttr.Value = "1";
                scriptNode.Attributes.Append(versionAttr);
                scriptNode.AppendChild(doc.CreateCDataSection(Source));
            }
            else if (existingScript is not null)
            {
                fn.RemoveChild(existingScript);
            }

            // Same for <variables>: only write when there are declared variables.
            var declaredVars = Variables.Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
            if (declaredVars.Count > 0)
            {
                XmlNode varsNode;
                if (existingVars is null)
                {
                    varsNode = doc.CreateElement("variables");
                    fn.AppendChild(varsNode);
                }
                else
                {
                    varsNode = existingVars;
                    varsNode.RemoveAll();
                }

                foreach (var v in declaredVars)
                {
                    var varNode = doc.CreateElement("var");
                    AppendAttr(doc, varNode, "name", v.Name);
                    AppendAttr(doc, varNode, "scope", v.Scope.ToString());
                    AppendAttr(doc, varNode, "type", v.Type.ToString());
                    AppendAttr(doc, varNode, "init", v.InitialValue);
                    if (v.IsOutput)
                    {
                        AppendAttr(doc, varNode, "output", "true");
                    }
                    varsNode.AppendChild(varNode);
                }
            }
            else if (existingVars is not null)
            {
                fn.RemoveChild(existingVars);
            }

            // persist user-assigned aspect data types (the aspect set itself is re-derived from couplings)
            var typedAspects = AspectVariables.Where(a => a.Type is not null).ToList();
            if (existingAspectTypes is not null) fn.RemoveChild(existingAspectTypes);
            if (typedAspects.Count > 0)
            {
                var atNode = doc.CreateElement("aspectTypes");
                foreach (var a in typedAspects)
                {
                    var aEl = doc.CreateElement("aspect");
                    AppendAttr(doc, aEl, "category", a.Category.ToString());
                    AppendAttr(doc, aEl, "label", a.Label);
                    AppendAttr(doc, aEl, "type", a.Type!.Value.ToString());
                    atNode.AppendChild(aEl);
                }
                fn.AppendChild(atNode);
            }
        }

        private static void AppendAttr(XmlDocument doc, XmlNode node, string name, string value)
        {
            var attr = doc.CreateAttribute(name);
            attr.Value = value;
            node.Attributes!.Append(attr);
        }
    }
}
