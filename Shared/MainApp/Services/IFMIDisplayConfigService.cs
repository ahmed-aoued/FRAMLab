using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
{
    public interface IFMIDisplayConfigService
    {
        Dictionary<string, string> DisplaySetup { get; }
        Dictionary<string, double> DisplayValue { get; }
        List<string> TypeFMIDisplay { get; }

        void SetTypeFMIDisplay(XmlDocument projectData, List<string> typeFMIdisplay);
        void UpdateDisplaySetup(XmlDocument projectData, string index, string attr, string value);
        void CheckDisplayValues(string index);
        void InitialDisplaySetup(XmlDocument projectData, string attr, string value, bool overwrite = false);
    }
}
