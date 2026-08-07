using System.Xml;

namespace FRAMLab.Shared.MainApp.Services
{
    public class UndoService : IUndoService
    {
        private const int MoveBack = 200;

        public int UndoLength { get; } = 700;
        public XmlDocument[] ProjectDataUndo { get; set; }
        public string[] SelectedFnUndo { get; set; }
        public int UndoIndex { get; set; }

        public UndoService()
        {
            ProjectDataUndo = new XmlDocument[UndoLength];
            SelectedFnUndo = new string[UndoLength];
            UndoIndex = 0;
        }

        public void UpdateUndo(string selectedFn)
        {
            if (ProjectDataUndo[0] is not null)
            {
                if (UndoIndex > UndoLength - 5)
                {
                    Array.Copy(ProjectDataUndo, MoveBack + 1, ProjectDataUndo, 1, UndoIndex - MoveBack);
                    Array.Copy(SelectedFnUndo, MoveBack + 1, SelectedFnUndo, 1, UndoIndex - MoveBack);
                    UndoIndex -= MoveBack;
                }

                UndoIndex += 1;
                ProjectDataUndo[UndoIndex] = (XmlDocument)ProjectDataUndo[0].Clone();
                SelectedFnUndo[UndoIndex] = selectedFn;
                ProjectDataUndo[UndoIndex + 1] = new XmlDocument();
                ProjectDataUndo[UndoIndex + 2] = new XmlDocument();
            }
        }
    }
}
