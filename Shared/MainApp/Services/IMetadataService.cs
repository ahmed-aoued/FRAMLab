using System.Data;
using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
{
    public interface IMetadataService
    {
        DataSet MetadataDS { get; set; }
        Dictionary<string, string> MetaDataKeys { get; set; }
        bool MetaIsDirty { get; set; }

        void CreateMetadataTable(XmlNode fn);
        void UpdateMetadata(XmlDocument projectData, Function selectedFunction);
        void AddMetadataResults(Function function, int cycleFMI);
        void CalcVariables(Function function, List<Coupling> couplingList, List<Function> functionList, int cycleFMI);
    }
}
