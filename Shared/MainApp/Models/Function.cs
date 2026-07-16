using FMV_OG.Shared;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Xml;
using System.Globalization;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Xml.Linq;
using System.ComponentModel.Design;
using System.Dynamic;
using System.Linq;

namespace FMV_OG.Shared
{
    public enum variableType : ushort
    {
        None = 0,
        String = 1,
        Int = 2,
        Double = 3,
        Bool = 4,
        Array1D = 5,
        Array2D = 6,
    }

    public class Function
    {
        public class VariableInfo
        {
            public string Name { get; set; } = "";
            public variableType Type { get; set; }
            public object Value { get; set; } = default!;
        }

        public ElementReference elementReference;
        private string _fnStyle;
        private string _FunctionType;
        private Dictionary<string, string[]> _variables = new Dictionary<string, string[]>();
        public List<VariableInfo> variableList = new List<VariableInfo>();

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

        public string AgentId { get; set; } = "";
        public string LevelId { get; set; } = "";
        public bool wasActive { get; set; } = false;

        public enum profileFMI : ushort
        {
            None = 0,
            All = 1,
            Any = 2,
        }
        public string profileFn { get; set; }
        // added string profile properties for backward‑compatibility
        public string profileI { get; set; } = "";
        public string profileP { get; set; } = "";
        public string profileR { get; set; } = "";
        public string profileC { get; set; } = "";
        public string profileT { get; set; } = "";

        public profileFMI PrInputs { get; set; }
        public profileFMI PrPreconditions { get; set; }
        public profileFMI PrResources { get; set; }
        public profileFMI PrControls { get; set; }

        // lists for FMI cycle tracking
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

        // ----------------------------------------
        // Legacy variable dictionary (string key -> string[] value)
        // used by FmiInterpreter / FMIControl for coupling-based variables
        // ----------------------------------------
        public void newVariable(string key, string[] value)
        {
            _variables[key] = value;
        }

        public string[]? getVariable(string key)
        {
            _variables.TryGetValue(key, out var val);
            return val;
        }

        public Dictionary<string, string[]> getVariables()
        {
            return _variables;
        }

        public void removeVariable(string prefix)
        {
            var keysToRemove = _variables.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var k in keysToRemove)
                _variables.Remove(k);
        }

        // ----------------------------------------
        // Legacy “variable” management for FMI profile (was ExpandoObject)
        // rewritten with a concrete class VariableInfo to avoid CS1061
        // ----------------------------------------
        public VariableInfo newVariable(string variableName, variableType type)
        {
            var varInfo = new VariableInfo { Name = variableName, Type = type };
            switch (type)
            {
                case variableType.String: varInfo.Value = ""; break;
                case variableType.Int: varInfo.Value = 0; break;
                case variableType.Double: varInfo.Value = 0.0; break;
                case variableType.Bool: varInfo.Value = false; break;
                case variableType.Array1D: varInfo.Value = new List<int>(); break;
                case variableType.Array2D: varInfo.Value = new List<List<int>>(); break;
                default: throw new ArgumentException("Invalid variable type");
            }
            variableList.Add(varInfo);
            return varInfo;
        }

        public void setVariable(VariableInfo varInfo, dynamic value)
        {
            switch (varInfo.Type)
            {
                case variableType.String: if (value is not string) throw new ArgumentException("Invalid value for String"); varInfo.Value = value; break;
                case variableType.Int: if (value is not int) throw new ArgumentException("Invalid value for Int"); varInfo.Value = value; break;
                case variableType.Double: if (value is not double) throw new ArgumentException("Invalid value for Double"); varInfo.Value = value; break;
                case variableType.Bool: if (value is not bool) throw new ArgumentException("Invalid value for Bool"); varInfo.Value = value; break;
                default: throw new ArgumentException("Invalid variable type");
            }
        }

        public void removeVariable(VariableInfo varInfo)
        {
            variableList.Remove(varInfo);
        }

        public VariableInfo? getVariable(string variableName)
        {
            return variableList.FirstOrDefault(v => v.Name == variableName);
        }

        public void resetVariables()
        {
            _variables.Clear();
            variableList.Clear();
        }

        // ------------------------------------------------------
        // remaining unchanged methods (isActive, resetAspects, resource, array, etc.)
        // ------------------------------------------------------
        private int countActive(List<Aspect> activeList)
        {
            int count = 0;
            foreach (var aspect in activeList)
                if (aspect.IsActive) count++;
            return count;
        }

        private void deactivateAspects(List<Aspect> aspectList)
        {
            foreach (var aspect in aspectList)
                aspect.IsActive = false;
        }

        public bool isActive()
        {
            if (inputs.Count == 0) return false;
            else
            {
                switch (PrInputs)
                {
                    case profileFMI.None: if (countActive(inputs) < inputs.Count) return false; break;
                    case profileFMI.Any: if (countActive(inputs) < inputs.Count) return false; break;
                    case profileFMI.All: if (inputs.Count > 0 && countActive(inputs) == 0) return false; break;
                }
                switch (PrPreconditions)
                {
                    case profileFMI.None: if (countActive(preconditions) < preconditions.Count) return false; break;
                    case profileFMI.Any: if (countActive(preconditions) < preconditions.Count) return false; break;
                    case profileFMI.All: if (preconditions.Count > 0 && countActive(preconditions) == 0) return false; break;
                }
                switch (PrResources)
                {
                    case profileFMI.None: if (countActive(resources) < resources.Count) return false; break;
                    case profileFMI.Any: if (countActive(resources) < resources.Count) return false; break;
                    case profileFMI.All: if (resources.Count > 0 && countActive(resources) == 0) return false; break;
                }
                switch (PrControls)
                {
                    case profileFMI.None: if (countActive(controls) < controls.Count) return false; break;
                    case profileFMI.Any: if (countActive(controls) < controls.Count) return false; break;
                    case profileFMI.All: if (controls.Count > 0 && countActive(controls) == 0) return false; break;
                }
                return true;
            }
        }

        public void resetAspects()
        {
            deactivateAspects(inputs);
            deactivateAspects(preconditions);
            deactivateAspects(resources);
            deactivateAspects(controls);
            fmiHighlight = "";
            wasActive = false;
        }

        public void resetKvalues()
        {
            Kvalues = new Dictionary<string, double>();
            Cvalues = new Dictionary<string, string>();
        }
        public string returnTrue(string KeyName)
        {
            return $"1:{KeyName}";
        }

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
            foreach (var tWord in textWords)
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
                    if (lL < length) textLines[tL] += " ";
                    string[] splitLines = doTWord.Split(Environment.NewLine);
                    int sLi = 0;
                    foreach (var splitWord in splitLines)
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
                    if (lL < length) textLines[tL] += " ";
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

        public string array(string[] content)
        {
            var returnString = _variables[content[0]][0];
            List<string> valueArray = new List<string>();
            if (returnString.Length > 0) { valueArray = returnString.Split(",").ToList(); }
            for (int i = 0; i < valueArray.Count; i++)
            {
                if (valueArray[i].Length > 0) valueArray[i] = (int.Parse(valueArray[i]) + 1).ToString();
            }
            returnString = string.Join(",", valueArray);
            return returnString;
        }

        public string resource(string[] content)
        {
            var returnString = _variables[content[0]][0];
            List<int> valueArray = new List<int>();
            if (returnString.Length > 0) { valueArray = returnString.Split(",", StringSplitOptions.TrimEntries).ToList().ConvertAll(Convert.ToInt32); }
            List<int> queueArray = new List<int>();
            var fnMDQ = metadata.Find(x => x[0] == "Queue_Array");
            if (fnMDQ is not null)
            {
                if (fnMDQ[1].Length > 0) { queueArray = metadata.Find(x => x[0] == "Queue_Array")![1].Split(",", StringSplitOptions.TrimEntries).ToList().ConvertAll(Convert.ToInt32); }
            }
            for (int i = 0; i < valueArray.Count; i++) valueArray[i] += 1;
            if (fnMDQ is not null)
            {
                for (int i = 0; i < queueArray.Count; i++) queueArray[i] += 1;
            }
            if (metadata.Find(x => x[0] == "Process_Max") is not null) { metadata.Find(x => x[0] == "Process_Max")![1] = valueArray.Count == 0 ? "" : valueArray.Max().ToString(); }
            if (metadata.Find(x => x[0] == "Queue_Max") is not null) { metadata.Find(x => x[0] == "Queue_Max")![1] = queueArray.Count == 0 ? "" : queueArray.Max().ToString(); }

            if (valueArray.Count > 0 && _variables.ContainsKey("Process_Time"))
            {
                if (valueArray[0] >= int.Parse(_variables["Process_Time"][0]))
                {
                    valueArray.Remove(valueArray[0]);
                    if (metadata.Find(x => x[0] == "Activate") is not null)
                        metadata.Find(x => x[0] == "Activate")![1] = "1";
                }
                else
                {
                    if (metadata.Find(x => x[0] == "Activate") is not null)
                        metadata.Find(x => x[0] == "Activate")![1] = "0";
                }
            }

            if (fnMDQ is not null)
            {
                if (metadata.Find(x => x[0] == "New_Arrival") is not null)
                    if (metadata.Find(x => x[0] == "New_Arrival")![1] == "1") queueArray.Add(0);
                if (queueArray.Count > 0 && _variables.ContainsKey("Capacity"))
                {
                    if (valueArray.Count < int.Parse(_variables["Capacity"][0]))
                    {
                        valueArray.Add(0);
                        queueArray.Remove(queueArray[0]);
                    }
                }
                metadata.Find(x => x[0] == "Queue_Array")![1] = string.Join(",", queueArray);
            }
            else
            {
                if (metadata.Find(x => x[0] == "New_Arrival") is not null)
                    if (metadata.Find(x => x[0] == "New_Arrival")![1] == "1") valueArray.Add(0);
            }

            if (metadata.Find(x => x[0] == "Process_Count") is not null) { metadata.Find(x => x[0] == "Process_Count")![1] = valueArray.Count.ToString(); }
            if (metadata.Find(x => x[0] == "Queue_Count") is not null) { metadata.Find(x => x[0] == "Queue_Count")![1] = queueArray.Count.ToString(); }

            returnString = string.Join(",", valueArray);
            return returnString;
        }
    }
}
