using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
{
    public interface IProjectDocumentService
    {
        void DefaultFnAttributes(XmlDocument projectData, XmlNode fn);
        void SetFunctionIsInput(XmlDocument projectData, string selectedFn, Function? selectedFunction, string isInput);
        void SetFunctionOrphans(XmlDocument projectData, string selectedFn, Function? selectedFunction, int orphans);
        void SetFunctionTypeByConnections(XmlDocument projectData, string selectedFn, Function? selectedFunction);
        bool DeleteFunction(StateContainer stateContainer);
        void SetFunctionIdName(StateContainer stateContainer, string idName);
        void SetFunctionDescription(StateContainer stateContainer, string description);
        void SetFunctionScript(XmlDocument projectData, string functionId, FRAMLab.Shared.MainApp.Models.ScriptModel script);
        void UpdateFunctionXY(XmlDocument projectData, string selectedFn, double x, double y, string? functionId = null);
        void UpdateAspectXY(XmlDocument projectData, string selectedLabel, double x, double y, string directionX, string directionY);
        void ResetAspect(StateContainer stateContainer, string[] dictArray, double xO, double yO, double aX, double aY, int aFW);
        void SetFunctionType(XmlDocument projectData, string functionId, string functionType, Function? function = null);
    }
}
