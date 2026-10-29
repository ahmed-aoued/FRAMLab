using System.Xml;

namespace FRAMLab.Shared.MainApp.Models
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
        /// <paramref name="declared"/> optionally supplies the aspects the function declares
        /// in the model (including unconnected ones); without it only coupled aspects exist.
        /// </summary>
        public void SyncAspects(IEnumerable<Coupling> couplings, string functionIdNr,
            IEnumerable<AspectVariable>? declared = null)
        {
            var derived = AspectVariable.DeriveFor(functionIdNr, couplings, declared);
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
                model.Variables.AddRange(ReadVars(varsNode, ScriptScope.Temp));
            }

            model.ReadAspectTypes(fn);
            return model;
        }

        private static List<ScriptVariable> ReadVars(XmlNode container, ScriptScope defaultScope)
        {
            var result = new List<ScriptVariable>();
            foreach (XmlNode varNode in container.SelectNodes("var")!)
            {
                var v = new ScriptVariable
                {
                    Name = varNode.SelectSingleNode("@name")?.Value ?? "",
                    InitialValue = varNode.SelectSingleNode("@init")?.Value ?? "",
                    IsOutput = varNode.SelectSingleNode("@output")?.Value == "true",
                    IsArray = varNode.SelectSingleNode("@array")?.Value == "true",
                    Dimensions = varNode.SelectSingleNode("@dims")?.Value == "2" ? 2 : 1,
                    Scope = defaultScope
                };
                if (Enum.TryParse<ScriptScope>(varNode.SelectSingleNode("@scope")?.Value ?? defaultScope.ToString(), true, out var scope))
                {
                    v.Scope = scope;
                }
                if (Enum.TryParse<ScriptVarType>(varNode.SelectSingleNode("@type")?.Value ?? "REAL", true, out var type))
                {
                    v.Type = type;
                }
                if (int.TryParse(varNode.SelectSingleNode("@lower")?.Value, out var lower)) v.LowerBound = lower;
                if (int.TryParse(varNode.SelectSingleNode("@upper")?.Value, out var upper)) v.UpperBound = upper;
                if (v.IsArray && v.UpperBound < v.LowerBound) v.UpperBound = v.LowerBound;
                if (v.Name != "")
                {
                    result.Add(v);
                }
            }
            return result;
        }

        private static XmlElement WriteVar(XmlDocument doc, ScriptVariable v)
        {
            var varNode = doc.CreateElement("var");
            AppendAttr(doc, varNode, "name", v.Name);
            AppendAttr(doc, varNode, "scope", v.Scope.ToString());
            AppendAttr(doc, varNode, "type", v.Type.ToString());
            if (!string.IsNullOrEmpty(v.InitialValue))
            {
                AppendAttr(doc, varNode, "init", v.InitialValue);
            }
            if (v.IsOutput)
            {
                AppendAttr(doc, varNode, "output", "true");
            }
            if (v.IsArray)
            {
                AppendAttr(doc, varNode, "array", "true");
                if (v.IsTable) AppendAttr(doc, varNode, "dims", "2");
                if (v.HasLegacyBounds)
                {
                    // arrays have no size; bounds are only kept when an older file declared them
                    AppendAttr(doc, varNode, "lower", v.LowerBound.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    AppendAttr(doc, varNode, "upper", v.UpperBound.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return varNode;
        }

        /// <summary>Model-wide script globals (the IDE's Global variables panel), stored as
        /// &lt;FM&gt;&lt;ScriptGlobals&gt;&lt;var .../&gt;&lt;/ScriptGlobals&gt;. Empty when absent.</summary>
        public static List<ScriptVariable> ReadGlobals(XmlDocument doc)
        {
            if (doc.SelectSingleNode("//FM/ScriptGlobals") is not XmlNode node)
            {
                return new List<ScriptVariable>();
            }
            var globals = ReadVars(node, ScriptScope.Global);
            foreach (var g in globals) g.Scope = ScriptScope.Global;
            return globals;
        }

        /// <summary>Write the model-wide script globals; removes the node when there are none,
        /// so models without globals save unchanged.</summary>
        public static void WriteGlobals(XmlDocument doc, IEnumerable<ScriptVariable> globals)
        {
            if (doc.SelectSingleNode("//FM") is not XmlNode fm) return;
            if (fm.SelectSingleNode("ScriptGlobals") is XmlNode existing) fm.RemoveChild(existing);
            var declared = globals.Where(v => !string.IsNullOrWhiteSpace(v.Name)).ToList();
            if (declared.Count == 0) return;
            var node = doc.CreateElement("ScriptGlobals");
            foreach (var v in declared)
            {
                v.Scope = ScriptScope.Global;
                node.AppendChild(WriteVar(doc, v));
            }
            fm.AppendChild(node);
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
                    varsNode.AppendChild(WriteVar(doc, v));
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
