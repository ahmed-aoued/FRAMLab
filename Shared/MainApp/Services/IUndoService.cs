using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
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
