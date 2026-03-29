using System.Xml;

namespace FMV_OG.Shared.MainApp.Services
{
    public interface IUndoService
    {
        XmlDocument[] ProjectDataUndo { get; set; }
        string[] SelectedFnUndo { get; set; }
        int UndoIndex { get; set; }
        int UndoLength { get; }

        void UpdateUndo(string selectedFn);
    }
}
