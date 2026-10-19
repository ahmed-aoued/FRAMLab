using System.Text.RegularExpressions;
using System.Xml;

namespace FRAMLab.Shared
{
    public enum FmiVariableScope { Cycle, Function, Model, Input, Output }

    /// <summary>
    /// A typed variable declared in the &lt;FMI&gt;&lt;Variables&gt; section of an .xfmv file.
    /// </summary>
    public sealed class FmiVariableDefinition
    {
        public static readonly Regex NamePattern = new("^[A-Z][A-Za-z0-9_]*$"); //same key rules as metadata keys

        public string Name { get; set; } = "";
        public FmiTypeDescriptor Type { get; set; } = FmiTypeDescriptor.String;
        public FmiVariableScope Scope { get; set; } = FmiVariableScope.Function;
        public string InitialRaw { get; set; } = "";
        public FmiValue InitialValue { get; set; } = FmiValue.Empty;
        public bool Persist { get; set; }
        public string FunctionId { get; set; } = ""; //"" = template: applies to every function (function/cycle scope)
        public string Description { get; set; } = "";
        public bool IsImplicit { get; set; } //derived from legacy metadata at load time, never serialized

        public static FmiVariableDefinition? FromXml(XmlElement el, List<FmiDiagnostic> diagnostics)
        {
            var name = el.GetAttribute("name");
            var source = $"FMI/Variables/Variable[{(name == "" ? "?" : name)}]";
            if (!NamePattern.IsMatch(name))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, "Variable name must match [A-Z][A-Za-z0-9_]*."));
                return null;
            }
            if (!FmiTypeDescriptor.TryParse(el.GetAttribute("type"), out var type, out var typeError))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, typeError!));
                return null;
            }
            var scopeRaw = el.GetAttribute("scope");
            if (!Enum.TryParse<FmiVariableScope>(scopeRaw, true, out var scope))
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Error, source, $"Unknown scope '{scopeRaw}'. Expected cycle, function, model, input or output."));
                return null;
            }
            var persist = el.GetAttribute("persist") == "true";
            if (persist && scope is FmiVariableScope.Cycle or FmiVariableScope.Input or FmiVariableScope.Output)
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, source, $"persist=\"true\" is ignored for scope '{scopeRaw}'."));
                persist = false;
            }
            var functionId = el.GetAttribute("function");
            if (scope == FmiVariableScope.Model && functionId != "")
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, source, "function attribute is ignored for model scope."));
                functionId = "";
            }
            var initialRaw = el.GetAttribute("initial");
            var initialValue = FmiValue.FromLegacyString(initialRaw, type, out var coerced);
            if (!coerced)
            {
                diagnostics.Add(new FmiDiagnostic(FmiDiagnosticSeverity.Warning, source, $"Initial value '{initialRaw}' does not match type '{type}'."));
            }
            return new FmiVariableDefinition
            {
                Name = name,
                Type = type!,
                Scope = scope,
                InitialRaw = initialRaw,
                InitialValue = initialValue,
                Persist = persist,
                FunctionId = functionId,
                Description = el.GetAttribute("description")
            };
        }

        public XmlElement ToXml(XmlDocument doc)
        {
            //Implicit definitions are derived from legacy metadata and must never be serialized;
            //FmiDefinitionService is the only writer and never passes implicit definitions here.
            System.Diagnostics.Debug.Assert(!IsImplicit, "Implicit definitions are never serialized.");
            var el = doc.CreateElement("Variable");
            el.SetAttribute("name", Name);
            el.SetAttribute("type", Type.ToString());
            el.SetAttribute("scope", Scope.ToString().ToLowerInvariant());
            if (FunctionId != "") el.SetAttribute("function", FunctionId);
            if (InitialRaw != "") el.SetAttribute("initial", InitialRaw);
            if (Persist) el.SetAttribute("persist", "true");
            if (Description != "") el.SetAttribute("description", Description);
            return el;
        }
    }
}
