using FMV_OG.Shared;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Xml;
using System.Globalization;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Xml.Linq;
using System.ComponentModel.Design;

namespace FMV_OG.Shared
{
    public class Function
    {
        public ElementReference elementReference;
        private string _fnStyle;
        private string _FunctionType;
        private Dictionary<string, string[]> _variables = new Dictionary<string, string[]>();
        public Function(XmlNode fn)
        {
            this.IDNr = fn.SelectSingleNode("IDNr")!.InnerText;
            var _x = fn.SelectSingleNode("@x")?.Value ?? "50";
            if (_x == "") _x = "50";
            this.x = double.Parse(_x, CultureInfo.InvariantCulture);
            var _y = fn.SelectSingleNode("@y")?.Value ?? "50";
            if (_y == "") _y = "50";
            this.y = double.Parse(_y, CultureInfo.InvariantCulture);
            _fnStyle = fn.SelectSingleNode("@fnStyle")?.Value ?? "0";
            _FunctionType = fn.SelectSingleNode("FunctionType")?.InnerText ?? "2";
            this.options = $"{_fnStyle}:{_FunctionType}:";
            this.IDName = fn.SelectSingleNode("IDName")?.InnerText ?? "";
            this.orphans = int.Parse(fn.SelectSingleNode("@orphans")?.Value ?? "0");
            this.isInput = fn.SelectSingleNode("@isInput")?.Value ?? "false";
            this.fnColorStyle = fn.SelectSingleNode("@style")?.Value ?? "";
            this.fnColorValue = fn.SelectSingleNode("@color")?.Value ?? "";
            this.profileFn = fn.SelectSingleNode("@profileFn")?.Value ?? "";
            // Alte Profil-Zeichenketten (in XML vorhanden) werden vorerst weiter übernommen,
            // damit der Ladevorgang nicht bricht.
            var profileIStr = fn.SelectSingleNode("@profileI")?.Value ?? "";
            var profilePStr = fn.SelectSingleNode("@profileP")?.Value ?? "";
            var profileRStr = fn.SelectSingleNode("@profileR")?.Value ?? "";
            var profileCStr = fn.SelectSingleNode("@profileC")?.Value ?? "";
            var profileTStr = fn.SelectSingleNode("@profileT")?.Value ?? "";

            this.PrInputs = ParseProfile(profileIStr, false);
            this.PrPreconditions = ParseProfile(profilePStr, true);
            this.PrResources = ParseProfile(profileRStr, true);
            this.PrControls = ParseProfile(profileCStr, true);
            // Time wird derzeit nicht als eigenes Profil verwaltet (bleibt bei alten String‑Eigenschaften).
            this.groupType = fn.SelectSingleNode("@groupType")!;
            this.groupIDNr = fn.SelectSingleNode("@groupIDNr")!;
            this.groupActive = fn.SelectSingleNode("@groupActive")!;
            this.groupX = fn.SelectSingleNode("@groupX")!;
            this.groupY = fn.SelectSingleNode("@groupY")!;
            this.metadata = new List<string[]>();
            if (fn.SelectSingleNode("metadata") is not null)
            {
                foreach (XmlElement key in fn.SelectSingleNode("metadata")!)
                {
                    this.metadata.Add(new string[] { key.Name, key.InnerText, key.Attributes["active"]?.Value ?? "", key.Attributes["equation"]?.Value ?? "", key.InnerText });
                }
            }
            this.AgentId = fn.SelectSingleNode("@agentId")?.Value ?? "";
            this.LevelId = fn.SelectSingleNode("@levelId")?.Value ?? "";
            this.Description = fn.SelectSingleNode("Description")?.InnerText ?? "";
        }

        // Hilfsmethode für die Konvertierung von XML‑String‑Profilen in das enum
        private static profileFMI ParseProfile(string raw, bool allowNone)
        {
            if (string.IsNullOrEmpty(raw)) return profileFMI.All;
            if (raw.Equals("All", StringComparison.OrdinalIgnoreCase)) return profileFMI.All;
            if (raw.Equals("Any", StringComparison.OrdinalIgnoreCase)) return profileFMI.Any;
            if (allowNone && raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return profileFMI.None;
            // Fallback: All (wie bisher)
            return profileFMI.All;
        }

        public List<string[]> metadata { get; set; }

        public List<Aspect> inputs { get; set; } = new List<Aspect>();
        public List<Aspect> outputs { get; set; } = new List<Aspect>();
        public List<Aspect> resources { get; set; } = new List<Aspect>();
        public List<Aspect> preconditions { get; set; } = new List<Aspect>();
        public List<Aspect> controls { get; set; } = new List<Aspect>();
        public int time { get; set; } = 0;
        public string options { get; set; }
        public string IDNr { get; set; }
        public int orphans { get; set; }
        public string isInput { get; set; }
        public string fnColorStyle { get; set; }
        public string fnColorValue { get; set; }
        public double x { get; set; }
        public double y { get; set; }
        public string fmiType { get; set; } = "";
        public string fmiHighlight { get; set; } = "";

        // Die ursprünglichen Listen für die aktiven Aspekte (werden weiter genutzt, solange die
        // vollständige Umstellung auf List<Aspect> noch nicht abgeschlossen ist).
        public List<string> activeI { get; set; } = new List<string>();
        public List<string> activeP { get; set; } = new List<string>();
        public List<string> activeR { get; set; } = new List<string>();
        public List<string> activeC { get; set; } = new List<string>();
        public List<string> activeT { get; set; } = new List<string>();
        public List<string> totalI { get; set; } = new List<string>();
        public List<string> totalP { get; set; } = new List<string>();
        public List<string> totalR { get; set; } = new List<string>();
        public List<string> totalC { get; set; } = new List<string>();
        public List<string> totalT { get; set; } = new List<string>();

        public string AgentId { get; set; } = "";
        public string LevelId { get; set; } = "";
        public bool wasActive { get; set; } = false;

        public enum profileFMI : ushort
        {
            One = 0,
            
            All = 1,

            Any = 2,

            /// <summary>
            /// Aspekt wird bei der Aktivierungsprüfung ignoriert (entspricht dem früheren „None“).
            /// </summary>
            None = 3
        }
        public string profileFn { get; set; }
        public profileFMI PrInputs { get; set; }
        public profileFMI PrPreconditions { get; set; }
        public profileFMI PrResources { get; set; }
        public profileFMI PrControls { get; set; }

        /* Old Profil Handling
        public string profileI { get; set; }
        public string profileP { get; set; }
        public string profileR { get; set; }
        public string profileC { get; set; }
        public string profileT { get; set; }
        */
        public XmlNode? group { get; set; }
        public XmlNode groupType { get; set; }
        public XmlNode groupIDNr { get; set; }
        public XmlNode groupActive { get; set; }
        public XmlNode groupX { get; set; }
        public XmlNode groupY { get; set; }
        public double startX { get; set; } = 0;
        public double startY { get; set; } = 0;
        public Dictionary<string, double> Kvalues { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, string> Cvalues { get; set; } = new Dictionary<string, string>();
        public string toolTip { get; set; } = "";
        public bool dragFn { get; set; } = false;
        public string fnStyle
        {
            get { return _fnStyle; }
            set
            {
                _fnStyle = value;
                options = $"{_fnStyle}:{_FunctionType}:";
            }
        }
        public string FunctionType
        {
            get { return _FunctionType; }
            set
            {
                _FunctionType = value;
                options = $"{_fnStyle}:{_FunctionType}:";
            }
        }
        public string IDName { get; set; }
        public string Description { get; set; }
        public List<string> ReturnTextLines(int length)
        {
            var textLines = new List<string>();
            string[] textWords = IDName.Split(" ");
            int tL = 0;
            textLines.Add("");
            int lL = length;
            foreach (string tWord in textWords)
            {
                var doTWord = tWord;
                if (doTWord.StartsWith(Environment.NewLine))
                {
                    textLines.Add("");
                    tL++;
                    lL = length;
                    doTWord = tWord.Replace(Environment.NewLine, "");
                }
                if (doTWord.Contains(Environment.NewLine) && !doTWord.EndsWith(Environment.NewLine))
                {
                    if (lL < length)
                    {
                        textLines[tL] += " ";
                    }
                    string[] splitLines = doTWord.Split(Environment.NewLine);
                    int sLi = 0;
                    foreach (string splitWord in splitLines)
                    {
                        doTWord = splitWord;
                        if (sLi < splitLines.Length - 1)
                        {
                            textLines[tL] += splitWord;
                            textLines.Add("");
                            tL++;
                            lL = length;
                        }
                        sLi++;
                    }
                }
                if (doTWord.Length <= lL)
                {
                    if (lL < length)
                    {
                        textLines[tL] += " ";
                    }
                    textLines[tL] += doTWord;
                    lL -= doTWord.Length;
                    if (doTWord.EndsWith(Environment.NewLine))
                    {
                        textLines.Add("");
                        tL++;
                        lL = length;
                    }
                }
                else if (doTWord.Length > length)
                {
                    string tempWord = doTWord;
                    while (tempWord.Length > 0)
                    {
                        if (tL > 0 || tempWord.Length > length + 1)
                        {
                            textLines.Add("");
                            tL++;
                        }
                        lL = length;
                        string addWord = tempWord.Substring(0, Math.Min(length + 1, tempWord.Length));
                        textLines[tL] += addWord;
                        lL -= addWord.Length;
                        tempWord = tempWord.Substring(addWord.Length);
                    }
                }
                else
                {
                    textLines.Add("");
                    tL++;
                    textLines[tL] += doTWord;
                    lL = length - doTWord.Length;
                }
            }
            return textLines;
        }


        // <summary>
        // All methods to be used in FMI
        public bool isActive()
        {
            // Keine Inputs → niemals aktiv
            if (inputs.Count == 0)
            {
                return false;
            }

            // Input-Profil überprüfen
            switch (PrInputs)
            {
                case profileFMI.All:
                case profileFMI.One:
                    if (activeI.Count < totalI.Count) return false;
                    break;
                case profileFMI.Any:
                    if (totalI.Count > 0 && activeI.Count == 0) return false;
                    break;
                case profileFMI.None:
                    // Nichts prüfen
                    break;
                default:
                    if (activeI.Count < totalI.Count) return false;
                    break;
            }

            // Precondition (falls nicht None)
            if (PrPreconditions != profileFMI.None)
            {
                switch (PrPreconditions)
                {
                    case profileFMI.All:
                    case profileFMI.One:
                        if (activeP.Count < totalP.Count) return false;
                        break;
                    case profileFMI.Any:
                        if (totalP.Count > 0 && activeP.Count == 0) return false;
                        break;
                    default:
                        if (activeP.Count < totalP.Count) return false;
                        break;
                }
            }

            // Resource
            if (PrResources != profileFMI.None)
            {
                switch (PrResources)
                {
                    case profileFMI.All:
                    case profileFMI.One:
                        if (activeR.Count < totalR.Count) return false;
                        break;
                    case profileFMI.Any:
                        if (totalR.Count > 0 && activeR.Count == 0) return false;
                        break;
                    default:
                        if (activeR.Count < totalR.Count) return false;
                        break;
                }
            }

            // Control
            if (PrControls != profileFMI.None)
            {
                switch (PrControls)
                {
                    case profileFMI.All:
                    case profileFMI.One:
                        if (activeC.Count < totalC.Count) return false;
                        break;
                    case profileFMI.Any:
                        if (totalC.Count > 0 && activeC.Count == 0) return false;
                        break;
                    default:
                        if (activeC.Count < totalC.Count) return false;
                        break;
                }
            }

            // Time wird momentan immer als erfüllt angesehen (früheres Verhalten beibehalten)
            return true;
        }
