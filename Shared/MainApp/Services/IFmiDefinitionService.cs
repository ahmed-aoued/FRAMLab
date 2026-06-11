using System.Xml;

namespace FMV_OG.Shared.MainApp.Services
{
    /// <summary>
    /// Owns reading and writing the &lt;FMI&gt; section of the project XML
    /// (variables now; rules and macros in later phases).
    /// </summary>
    public interface IFmiDefinitionService
    {
        FmiModelDefinition Load(XmlDocument projectData);
        XmlNode EnsureFmiRoot(XmlDocument projectData);
        void UpsertVariable(XmlDocument projectData, FmiVariableDefinition def);
        void RemoveVariable(XmlDocument projectData, string name, string functionId);
        FmiModelDefinition BuildImplicitDefinitions(List<Function> functions);
    }
}
