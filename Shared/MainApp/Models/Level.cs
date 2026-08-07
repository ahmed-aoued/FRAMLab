using System.Xml;

namespace FRAMLab.Shared
{
    public class Level
    {
        public Level(string idNr, string name, int hierarchy = 0, string levelType = "")
        {
            this.IDNr = idNr;
            this.Name = name;
            this.LevelType = levelType;
            this.Hierarchy = hierarchy;
            this.Description = "";
            this.Color = "";
            this.IsActive = true;
            this.TimeScale = "";
            this.DecisionScope = "";
            this.Metadata = new List<string[]>();
        }

        public Level(XmlNode node)
        {
            this.IDNr = node.SelectSingleNode("IDNr")?.InnerText ?? "";
            this.Name = node.SelectSingleNode("Name")?.InnerText ?? "";
            this.Description = node.SelectSingleNode("Description")?.InnerText ?? "";
            this.LevelType = node.SelectSingleNode("LevelType")?.InnerText ?? "Operational";
            this.Hierarchy = int.Parse(node.SelectSingleNode("Hierarchy")?.InnerText ?? "0");
            this.Color = node.SelectSingleNode("@color")?.Value ?? "";
            this.IsActive = node.SelectSingleNode("@isActive")?.Value == "true";
            this.TimeScale = node.SelectSingleNode("TimeScale")?.InnerText ?? "";
            this.DecisionScope = node.SelectSingleNode("DecisionScope")?.InnerText ?? "";
            
            this.Metadata = new List<string[]>();
            if (node.SelectSingleNode("metadata") is not null)
            {
                foreach (XmlElement key in node.SelectSingleNode("metadata")!)
                {
                    this.Metadata.Add(new string[] { key.Name, key.InnerText, key.Attributes["active"]?.Value ?? "", key.Attributes["equation"]?.Value ?? "", key.InnerText });
                }
            }
        }

        public string IDNr { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string LevelType { get; set; }
        public int Hierarchy { get; set; }
        public string Color { get; set; }
        public bool IsActive { get; set; }
        public string TimeScale { get; set; }
        public string DecisionScope { get; set; }
        public List<string[]> Metadata { get; set; }
    }
}
