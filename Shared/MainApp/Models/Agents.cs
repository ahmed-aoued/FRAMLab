using System.Xml;

namespace FMV_OG.Shared
{
    public class Agent
    {
        public Agent(string idNr, string name, string? agentType)
        {
            IDNr = idNr;
            Name = name;
            AgentType = agentType ?? "";
            Description = "";
            Color = "";
            IsActive = true;
            Metadata = new List<string[]>();
        }

        public Agent(XmlNode node)
        {
            IDNr = node.SelectSingleNode("IDNr")?.InnerText ?? "";
            Name = node.SelectSingleNode("Name")?.InnerText ?? "";
            AgentType = node.SelectSingleNode("AgentType")?.InnerText ?? "";
            Color = node.SelectSingleNode("Color")?.InnerText ?? "";
            IsActive = bool.Parse(node.SelectSingleNode("IsActive")?.InnerText ?? "true");
            Description = node.SelectSingleNode("Description")?.InnerText ?? "";

            Metadata = new List<string[]>();
            if (node.SelectSingleNode("metadata") is not null)
            {
                foreach (XmlElement key in node.SelectSingleNode("metadata")!)
                {
                    Metadata.Add(new string[] { key.Name, key.InnerText, key.Attributes["active"]?.Value ?? "", key.Attributes["equation"]?.Value ?? "", key.InnerText });
                }
            }
        }

        public string IDNr { get; set; }
        public string Name { get; set; }
        public string AgentType { get; set; }
        public string Description { get; set; }
        public string Color { get; set; }
        public bool IsActive { get; set; }
        public List<string[]> Metadata { get; set; }

        public void AddMetadata(string key, string value, string active = "", string equation = "")
        {
            var existingMeta = Metadata.Find(x => x[0] == key);
            if (existingMeta != null)
            {
                existingMeta[1] = value;
                existingMeta[2] = active;
                existingMeta[3] = equation;
                existingMeta[4] = value;
            }
            else
            {
                Metadata.Add(new string[] { key, value, active, equation, value });
            }
        }

        public string? GetMetadataValue(string key)
        {
            var meta = Metadata.Find(x => x[0] == key);
            return meta?[1];
        }
    }
}
