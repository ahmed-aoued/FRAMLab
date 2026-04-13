using System.Xml;

namespace FMV_OG.Shared
{
    public class Level
    {
        public Level(string idNr, string name, int hierarchy = 0)
        {
            IDNr = idNr;
            Name = name;
            Hierarchy = hierarchy;
        }

        public Level(XmlNode node)
        {
            IDNr = node.SelectSingleNode("IDNr")?.InnerText ?? "";
            Name = node.SelectSingleNode("Name")?.InnerText ?? "";
            Hierarchy = int.TryParse(node.SelectSingleNode("Hierarchy")?.InnerText, out var h) ? h : 0;
        }

        public string IDNr { get; set; }
        public string Name { get; set; }
        public int Hierarchy { get; set; }
    }
}
