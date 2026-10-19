using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
{
    /// <summary>
    /// Owns reading and writing the &lt;FMI&gt; section of the project XML
    /// (variables and macros).
    /// </summary>
    public interface IFmiDefinitionService
    {
        FmiModelDefinition Load(XmlDocument projectData);
        FmiModelDefinition MergeWithImplicit(FmiModelDefinition declared, List<Function> functions);
        XmlNode EnsureFmiRoot(XmlDocument projectData);
        void UpsertVariable(XmlDocument projectData, FmiVariableDefinition def);
        void RemoveVariable(XmlDocument projectData, string name, string functionId);
        /// <summary>Adds or replaces the macro by (Name, FunctionId). Returns false when the name
        /// is already used by a macro in another scope (no shadowing), leaving the document unchanged.</summary>
        bool UpsertMacro(XmlDocument projectData, FmiMacroDefinition macro);
        bool RemoveMacro(XmlDocument projectData, string name, string functionId);
        FmiModelDefinition BuildImplicitDefinitions(List<Function> functions);
    }
}
