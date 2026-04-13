using System.Xml;

namespace FMV_OG.Shared
{
    public class Agent
    {
        public Agent(string idNr, string name, string agentType = "")
        {
            IDNr = idNr;
            Name = name;
            AgentType = agentType;
        }

        public Agent(XmlNode node)
        {
            IDNr = node.SelectSingleNode("IDNr")?.InnerText ?? "";
            Name = node.SelectSingleNode("Name")?.InnerText ?? "";
            AgentType = node.SelectSingleNode("AgentType")?.InnerText ?? "";
        }

        public string IDNr { get; set; }
        public string Name { get; set; }
        public string AgentType { get; set; }
    }
}
