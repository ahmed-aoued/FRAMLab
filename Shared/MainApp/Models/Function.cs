using FRAMLab.Shared;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Xml;
using System.Globalization;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Xml.Linq;
using System.ComponentModel.Design;
using System.Dynamic;
using System.Linq;

namespace FRAMLab.Shared
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
            // Legacy XML attributes -> enum (backward compatible)
            this.PrInputs = ParseProfile(fn.SelectSingleNode("@profileI")?.Value);
            this.PrPreconditions = ParseProfile(fn.SelectSingleNode("@profileP")?.Value);
            this.PrResources = ParseProfile(fn.SelectSingleNode("@profileR")?.Value);
            this.PrControls = ParseProfile(fn.SelectSingleNode("@profileC")?.Value);
            // Note: @profileT (Time) is intentionally not mapped to an enum property.
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
            this.Script = FRAMLab.Shared.MainApp.Models.ScriptModel.FromXml(fn);
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
        /*
        public string profileI { get; set; } = "";
        public string profileP { get; set; } = "";
        public string profileR { get; set; } = "";
        public string profileC { get; set; } = "";
        public string profileT { get; set; } = "";
        */

        public profileFMI PrInputs { get; set; }
        public profileFMI PrPreconditions { get; set; }
        public profileFMI PrResources { get; set; }
        public profileFMI PrControls { get; set; }

        // ----------------------------------------
        // Helper: translate legacy XML profile strings
        // ("All" / "Any" / "None" / "") into profileFMI enum.
        // Empty string defaults to All (matches old UI behaviour).
        // ----------------------------------------
        public static profileFMI ParseProfile(string? value)
        {
            return value switch
            {
                "All" => profileFMI.All,
                "Any" => profileFMI.Any,
                "None" => profileFMI.None,
                _ => profileFMI.All,
            };
        }

        public static string ProfileToString(profileFMI profile)
        {
            return profile switch
            {
                profileFMI.All => "All",
                profileFMI.Any => "Any",
                profileFMI.None => "None",
                _ => "",
            };
        }

        // lists for FMI cycle tracking
        /*
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

        */

        // ----------------------------------------
        // Legacy variable dictionary (string key -> string[] value)
        // used by FmiInterpreter / FMIControl for coupling-based variables
        // ----------------------------------------
        /*
        public void newVariable(string key, string[] value)
        {
            _variables[key] = value;
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
        */
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
                case variableType.String: if (value is not string) throw new ArgumentException("Invalid value for String");varInfo.Value = value; break;
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

        /// <summary>
        /// Dictionary view over <see cref="variableList"/> for the ST scripting engine,
        /// which reads the FMI pipeline's propagated values by key (e.g. "I0_Hazard_signal").
        /// Values are stringified exactly like the legacy dictionary the scripting code was
        /// written against; the typed variables remain authoritative for the engine.
        /// </summary>
        public Dictionary<string, string[]> getVariables()
        {
            var result = new Dictionary<string, string[]>();
            foreach (var variable in variableList)
            {
                result[variable.Name] = new[] { variable.Value?.ToString() ?? "", variable.Type.ToString() };
            }
            return result;
        }

        /// <summary>
        /// Legacy string-based write used by the ST scripting engine for script outputs.
        /// Upserts into <see cref="variableList"/> so both the typed FMI engine and the
        /// scripting layer see the same value.
        /// </summary>
        public void newVariable(string key, string[] value)
        {
            var existing = getVariable(key);
            if (existing is null)
            {
                existing = new VariableInfo { Name = key, Type = variableType.String };
                variableList.Add(existing);
            }
            existing.Value = value.Length > 0 ? value[0] : "";
        }

        public void resetVariables()
        {
            variableList.Clear();
        }

        // ------------------------------------------------------
        // remaining unchanged methods (isActive, resetAspects, resource, array, etc.)
        // ------------------------------------------------------
        
        /*
        private int countActive(List<Aspect> activeList)
        {
            int count = 0;
            foreach (var aspect in activeList)
                if (aspect.IsActive) count++;
            return count;
        }*/

        private int countActive(List<Aspect> activeList)
        {
            return activeList.Count(aspect => aspect.IsActive);
        }

        /*
        private void deactivateAspects(List<Aspect> aspectList)
        {
            foreach (var aspect in aspectList)
                aspect.IsActive = false;
        }*/

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
            time = 0;
            fmiHighlight = "";
            wasActive = false;
        }

        // ----------------------------------------
        // Helpers: activate / deactivate / ensure coupling aspects
        // Replaces the old activeI/P/R/C/T and totalI/P/R/C/T string lists.
        // ----------------------------------------
        private List<Aspect>? AspectListFor(string type) => type switch
        {
            "I" => inputs,
            "P" => preconditions,
            "R" => resources,
            "C" => controls,
            _ => null
        };

        public void ActivateCoupling(string type, string couplingName, string outputFnIDNr = "")
        {
            if (type == "T") { time++; return; }
            var list = AspectListFor(type);
            if (list == null) return;
            var a = list.FirstOrDefault(x => x.IDName == couplingName);
            if (a != null) a.IsActive = true;
            else list.Add(new Aspect(type, "", "", couplingName, outputFnIDNr) { IsActive = true });
        }

        public void DeactivateCoupling(string type, string couplingName)
        {
            if (type == "T") { time = Math.Max(0, time - 1); return; }
            var list = AspectListFor(type);
            if (list == null) return;
            var a = list.FirstOrDefault(x => x.IDName == couplingName);
            if (a != null) a.IsActive = false;
        }

        public void EnsureCoupling(string type, string couplingName)
        {
            if (type == "T") return;
            var list = AspectListFor(type);
            if (list == null) return;
            if (!list.Any(x => x.IDName == couplingName))
                list.Add(new Aspect(type, "", "", couplingName, "") { IsActive = false });
        }


        public void resetKvalues()
        {
            Kvalues = new Dictionary<string, double>();
            Cvalues = new Dictionary<string, string>();
        }

        /*
        public string returnTrue(string KeyName)
        {
            return $"1:{KeyName}";
        }*/

        //Group Function

        public XmlNode? group { get; set; }
        public XmlNode groupType { get; set; }
        public XmlNode groupIDNr { get; set; }
        public XmlNode groupActive { get; set; }
        public XmlNode groupX { get; set; }
        public XmlNode groupY { get; set; }
        public double startX { get; set; } = 0;
        public double startY { get; set; } = 0;

        //Whatever Kvalues and Cvalues are
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
        /// <summary>ST script attached to this function (source + declared variables + aspect types).</summary>
        public FRAMLab.Shared.MainApp.Models.ScriptModel Script { get; set; } = new FRAMLab.Shared.MainApp.Models.ScriptModel();
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
            var varInfo = getVariable(content[0]);
            var returnString = varInfo?.Value?.ToString() ?? "";
            List<string> valueArray = new List<string>();
            if (returnString.Length > 0) { valueArray = returnString.Split(",").ToList(); }
            for (int i = 0; i < valueArray.Count; i++)
            {
                if (valueArray[i].Length > 0) valueArray[i] = (int.Parse(valueArray[i]) + 1).ToString();
            }
            returnString = string.Join(",", valueArray);
            return returnString;
        }

        /*
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
        }*/
    }
}
