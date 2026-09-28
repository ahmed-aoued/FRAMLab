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

        public bool HasContent => !string.IsNullOrWhiteSpace(Source) || Variables.Count > 0;

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

            if (!HasContent)
            {
                if (existingScript is not null) fn.RemoveChild(existingScript);
                if (existingVars is not null) fn.RemoveChild(existingVars);
                return;
            }

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

            foreach (var v in Variables)
            {
                if (string.IsNullOrWhiteSpace(v.Name)) continue;
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

        private static void AppendAttr(XmlDocument doc, XmlNode node, string name, string value)
        {
            var attr = doc.CreateAttribute(name);
            attr.Value = value;
            node.Attributes!.Append(attr);
        }
    }
}
