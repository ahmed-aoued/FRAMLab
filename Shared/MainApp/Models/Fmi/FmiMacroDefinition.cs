using System.Text.RegularExpressions;
using System.Xml;

namespace FRAMLab.Shared
{
    public sealed class FmiMacroParameter
    {
        public string Name { get; set; } = ""; //[A-Za-z_][A-Za-z0-9_]* (lower-case camel ok, unlike variables)
        public FmiTypeDescriptor Type { get; set; } = FmiTypeDescriptor.Number;
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// A user-defined formula (macro) declared in the &lt;FMI&gt;&lt;Macros&gt; section of an .xfmv file:
    /// a named, parameterised expression that returns a value. Statement logic belongs in ST scripts;
    /// the former "rule" macro kind has been removed.
    /// </summary>
    public sealed class FmiMacroDefinition
    {
        // Distinct from variable PascalCase ([A-Z]…) so macro calls are visually unambiguous.
        public static readonly Regex NamePattern = new("^[a-z][A-Za-z0-9_]*$");
        public static readonly Regex ParameterNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$");

        public string Name { get; set; } = "";
        public List<FmiMacroParameter> Parameters { get; } = new();
        public string Body { get; set; } = ""; //a single expression
        public FmiTypeDescriptor? ReturnType { get; set; } //hint only; null = infer
        public string FunctionId { get; set; } = ""; //"" = global: visible to every function (same convention as FmiVariableDefinition)
        public string Description { get; set; } = "";
        public bool IsImplicit { get; set; } //reserved; macros are never implicit this phase

        public static FmiMacroDefinition? FromXml(XmlElement el, List<FmiDiagnostic> diagnostics)
        {
            var name = el.GetAttribute("name");
            var source = $"FMI/Macros/Macro[{(name == "" ? "?" : name)}]";
            if (!NamePattern.IsMatch(name))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, "Macro name must match [a-z][A-Za-z0-9_]*."));
                return null;
            }

            //Older files write kind="formula"; rule macros no longer exist.
            var kindRaw = el.GetAttribute("kind");
            if (kindRaw.Equals("rule", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source,
                    "Rule macros are no longer supported; move the statements into the function's ST script. This macro was skipped."));
                return null;
            }
            if (kindRaw != "" && !kindRaw.Equals("formula", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, $"Unknown kind '{kindRaw}'. Macros are formulas; omit the kind attribute."));
                return null;
            }

            FmiTypeDescriptor? returnType = null;
            var returnRaw = el.GetAttribute("return");
            if (returnRaw != "")
            {
                if (!FmiTypeDescriptor.TryParse(returnRaw, out var parsed, out var returnError))
                {
                    diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, returnError!));
                    return null;
                }
                returnType = parsed;
            }

            var parameters = new List<FmiMacroParameter>();
            foreach (XmlNode node in el.ChildNodes)
            {
                if (node is not XmlElement paramEl || paramEl.Name != "Param")
                {
                    continue;
                }
                var paramName = paramEl.GetAttribute("name");
                if (!ParameterNamePattern.IsMatch(paramName))
                {
                    diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, $"Parameter name '{paramName}' must match [A-Za-z_][A-Za-z0-9_]*."));
                    return null;
                }
                if (!FmiTypeDescriptor.TryParse(paramEl.GetAttribute("type"), out var paramType, out var paramTypeError))
                {
                    diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, paramTypeError!));
                    return null;
                }
                parameters.Add(new FmiMacroParameter
                {
                    Name = paramName,
                    Type = paramType!,
                    Description = paramEl.GetAttribute("description")
                });
            }

            var body = "";
            if (el.SelectSingleNode("Body") is XmlElement bodyEl)
            {
                body = bodyEl.InnerText;
            }
            if (string.IsNullOrWhiteSpace(body))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, "Formula macro body must not be empty."));
                return null;
            }

            var macro = new FmiMacroDefinition
            {
                Name = name,
                Body = body,
                ReturnType = returnType,
                FunctionId = el.GetAttribute("function"),
                Description = el.GetAttribute("description")
            };
            macro.Parameters.AddRange(parameters);
            return macro;
        }

        public XmlElement ToXml(XmlDocument doc)
        {
            var el = doc.CreateElement("Macro");
            el.SetAttribute("name", Name);
            if (ReturnType is not null) el.SetAttribute("return", ReturnType.ToString());
            if (FunctionId != "") el.SetAttribute("function", FunctionId);
            if (Description != "") el.SetAttribute("description", Description);
            foreach (var parameter in Parameters)
            {
                var paramEl = doc.CreateElement("Param");
                paramEl.SetAttribute("name", parameter.Name);
                paramEl.SetAttribute("type", parameter.Type.ToString());
                if (parameter.Description != "") paramEl.SetAttribute("description", parameter.Description);
                el.AppendChild(paramEl);
            }
            var bodyEl = doc.CreateElement("Body");
            bodyEl.InnerText = Body;
            el.AppendChild(bodyEl);
            return el;
        }
    }
}
