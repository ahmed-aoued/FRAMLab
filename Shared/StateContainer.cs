using FMV_OG.Shared;
using Microsoft.AspNetCore.Components;
using System.Data;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FMV_OG.Shared
{
    public class StateContainer
    {
        //Done
        private string _selectedFn = "-1";
        //Done
        private string _selectedLabel = "";
        private List<string> _typeFMIdisplay = new List<string> { "", "Blend", "Blend", "Blend", "Blend", "Blend", "Blend" };
        
        public bool showProperties = false;
        public bool showAspects = false;
        public bool showFMIProfile = false;
        public bool showMetadata = false;
        public Type? showKeysSetting = Type.GetType("FMV_OG.Shared.FMIControl"); // original = typeof(FMIControl); Use for alternative analysis
        public bool showMetaDetail = false; //If false, this hides the -active and -equation columns in the Metadata table view - This data is now displayed in the Tooltip, so keep false
        public string showKeys = "";
        public string lastKey = "";
        public int cycleFMI = -1;
        public Function? selectedFunction;
        public Coupling? selectedCoupling;
        public DataSet metadataDS = new DataSet();
        public Dictionary<string, string> metaDataKeys = new Dictionary<string, string>();
        public bool metaIsDirty = false;
        //Done
        public string fnClass { get; set; } = "fn-hover";
        public List<string[]> dataRows { get; set; } = new List<string[]>();
        public Dictionary<string, string> dataKeys { get; set; } = new Dictionary<string, string>();
        private Random rnd = new Random();
        public string selectedFn
        {
            get
            {
                return _selectedFn;
            }
            set
            {
                if (selectedFunction is not null)
                {
                    if (metaIsDirty)
                    {
                        updateMetadata(selectedFunction);
                    }
                    if (selectedFunction.groupIDNr.Value != "")
                    {
                        foreach (var fnG in functionList!.FindAll(x => x.groupIDNr.Value == selectedFunction.groupIDNr.Value))
                        {
                            fnG.groupType.Value = "";
                        }
                    }
                }
                _selectedFn = value;
                if (selectedMulti.Count > 0)
                {
                    foreach (var fnIDNr in selectedMulti)
                    {
                        var fn = functionList.Find(x => x.IDNr == fnIDNr);
                        if (fn is not null)
                        {
                            fn.dragFn = false;
                        }
                    }
                }
                selectedMulti = new List<string>();
                if (functionList is not null)
                {
                    selectedFunction = functionList.Find(x => x.IDNr == _selectedFn);
                }
                else
                {
                    selectedFunction = null;
                }
                if (_selectedFn != "-1" && selectedFunction is not null)
                {
                    selectedMulti.Add(value);
                    var parentIDNr = selectedFunction.groupIDNr.Value;
                    XmlNode? group = fnGroups!.SelectSingleNode($"Group[FunctionIDNr={_selectedFn}]");
                    if (group is not null)
                    {
                        selectedFunction.groupIDNr.Value = _selectedFn;
                        selectedFunction.group = group;
                        parentIDNr = _selectedFn;
                    }
                    else if (parentIDNr != "")
                    {
                        group = fnGroups!.SelectSingleNode($"Group[FunctionIDNr={parentIDNr}]");
                    }
                    if (parentIDNr != "" && group is not null)
                    {
                        if (group is not null)
                        {
                            functionList!.Find(x => x.IDNr == parentIDNr)!.groupType.Value = "parent";
                            foreach (var fnGID in group.SelectSingleNode("CHILD")!.InnerText.Split("|"))
                            {
                                var getFn = functionList.Find(x => x.IDNr == fnGID);
                                if (getFn is not null)
                                {
                                    if (!(getFn.groupActive.Value == "true" && getFn.groupIDNr.Value != "" && getFn.groupIDNr.Value != parentIDNr))
                                    {
                                        getFn.groupIDNr.Value = parentIDNr;
                                        getFn.groupType.Value = "group";
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    isDisabled = true;
                    fnName = "";
                }
                lastKey = "";
                showVariables = "";
            }
        }
        public string selectedLabel
        {
            get
            {
                return _selectedLabel;
            }
            set
            {
                _selectedLabel = value;
                if (couplingList is not null)
                {
                    selectedCoupling = couplingList.Find(x => x.Name == _selectedLabel);
                }
                else
                {
                    selectedCoupling = null;
                }
                if (_selectedLabel != "" && selectedCoupling is not null)
                {
                    selectedCoupling!.aspectClass = "fn-hover";
                }
            }
        }
        public string touchAction { get; set; } = "auto";
        public int newFnStyle { get; set; } = 0;
        public int spreadsheetVisible { get; set; } = 0;
        public string aspectLabelsDisplay { get; set; } = "";
        public double tempZoomF { get; set; } = 1;
        public double tempZoomA { get; set; } = 1;
        public double scaleZoom { get; set; } = 1.5;
        public double canvasWidth { get; set; }
        public double canvasHeight { get; set; }
        public double viewWidth { get; set; } = 0;
        public double viewHeight { get; set; } = 0;
        public XmlDocument[] projectData_Undo { get; set; } = new XmlDocument[700];
        public readonly int undoLength = 700;
        private readonly int moveBack = 200;
        public XmlNode? fnGroups { get; set; }
        public string[] selectedFn_Undo { get; set; } = new string[700];
        public int undoIndex { get; set; } = 0;
        public List<Coupling> couplingList { get; set; } = new List<Coupling>();
        public List<Function> functionList { get; set; } = new List<Function>();
        public string fnName { get; set; } = "";
        public bool isDisabled { get; set; } = true;
        public List<Aspect> aspectsList { get; set; } = new List<Aspect>();
        public List<Aspect> outputsList { get; set; } = new List<Aspect>();
        public bool showModal { get; set; } = false;
        public bool showColorPicker { get; set; } = false;
        public bool showFMIPopup { get; set; } = false;
        public bool showPDFreport { get; set; } = false;
        public bool showFMIDisplaySetup { get; set; } = false;
        public string fmiMessage { get; set; } = "";
        public string fileName { get; set; } = "FMV_new.xfmv";
        public bool fileLoaded { get; set; } = false;
        public string inputDropStatus { get; set; } = "file-input-zone-drop";
        public string debugOutput { get; set; } = "";
        public bool isBackup { get; set; } = false;
        public int cycleDelay { get; set; } = 750;
        public string showVariables { get; set; } = "";
        public double selectX { get; set; } = 0;
        public double selectY { get; set; } = 0;
        public double selectWidth { get; set; } = 0;
        public double selectHeight { get; set; } = 0;
        public List<string> selectedMulti { get; set; } = new List<string>();
        public bool isSelecting { get; set; } = false;
        public double startX { get; set; } = 0;
        public double startY { get; set; } = 0;
        public Dictionary<string, string> displaySetup = new Dictionary<string, string>();
        public Dictionary<string, double> displayValue = new Dictionary<string, double>();
        public bool hideFMIhighlight { get; set; } = false;
        public bool hideFMIdisplaytext { get; set; } = false;
        public List<string> typeFMIdisplay 
        {
            get 
            {
                return _typeFMIdisplay;
            } 
            set
            {
                _typeFMIdisplay = value;
                if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/@typeFMIdisplay") == null)
                {
                    projectData_Undo[0].SelectSingleNode("//FM/Functions")!.Attributes!.Append(projectData_Undo[0].CreateAttribute("typeFMIdisplay"));
                }
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/@typeFMIdisplay")!.Value = string.Join("|", _typeFMIdisplay);
            }
        }
        public void defaultFnAttributes(XmlNode fn)
        {
            if (fn.SelectSingleNode("@orphans") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("orphans"));
            }
            fn.SelectSingleNode("@orphans")!.Value = "0";
            if (fn.SelectSingleNode("@profileFn") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("profileFn"));
            }
            if (fn.SelectSingleNode("@profileI") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("profileI"));
            }
            if (fn.SelectSingleNode("@profileP") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("profileP"));
            }
            if (fn.SelectSingleNode("@profileR") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("profileR"));
            }
            if (fn.SelectSingleNode("@profileC") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("profileC"));
            }
            if (fn.SelectSingleNode("@profileT") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("profileT"));
            }
            if (fn.SelectSingleNode("@groupType") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("groupType"));
            }
            if (fn.SelectSingleNode("@groupIDNr") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("groupIDNr"));
            }
            if (fn.SelectSingleNode("@groupActive") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("groupActive"));
            }
            if (fn.SelectSingleNode("@groupX") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("groupX"));
            }
            if (fn.SelectSingleNode("@groupY") == null)
            {
                fn.Attributes!.Append(projectData_Undo[0].CreateAttribute("groupY"));
            }
            if (fn.SelectSingleNode("metadata") == null)
            {
                fn.AppendChild(projectData_Undo[0].CreateElement("metadata"));
            }
            if (fn.SelectSingleNode("Description") == null)
            {
                fn.AppendChild(projectData_Undo[0].CreateElement("Description"));
            }
        }
        public void defaultFnLabel()
        {
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/IDName")!.InnerText = _selectedFn;
            selectedFunction!.IDName = _selectedFn;
            fnName = _selectedFn;
        }
        public void sFnIsInput(string isInput)
        {
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/@isInput")!.InnerText = isInput;
            selectedFunction!.isInput = isInput;
        }
        public void sFnOrphans(int orphans)
        {
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/@orphans")!.InnerText = orphans.ToString();
            selectedFunction!.orphans = orphans;
        }
        public void sFnFunctionType()
        {
            var check1 = projectData_Undo[0].SelectNodes($"//FM/Preconditions/Precondition[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            check1 += projectData_Undo[0].SelectNodes($"//FM/Resources/Resource[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            check1 += projectData_Undo[0].SelectNodes($"//FM/Controls/Control[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            check1 += projectData_Undo[0].SelectNodes($"//FM/Times/Time[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            var check2 = projectData_Undo[0].SelectNodes($"//FM/Outputs/Output[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            var check3 = projectData_Undo[0].SelectNodes($"//FM/Inputs/Input[FunctionIDNr=\"{selectedFn}\"]")?.Count;
            if (check1 == 0 && (check2 == 0 || check3 == 0)) //has only inputs or only outputs
            {
              //background
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/FunctionType")!.InnerText = "2";
                selectedFunction!.FunctionType = "2";
            }
            else
            {
                //foreground
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/FunctionType")!.InnerText = "0";
                selectedFunction!.FunctionType = "0";
            }
        }
        public void deleteFn()
        {
            if (aspectsList.FindAll(x => x.FunctionIDNr == _selectedFn).Count == 0 && projectData_Undo[0].SelectNodes($"//FM//Outputs/Output[FunctionIDNr=\"{_selectedFn}\"]")?.Count == 0)
            {
                updateUndo();
                projectData_Undo[0].SelectSingleNode("//FM/Functions")!.RemoveChild(projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]")!);
                functionList.RemoveAll(x => x.IDNr == _selectedFn);
                couplingList.RemoveAll(x => x.outputFn == _selectedFn || x.toFn == _selectedFn);
                XmlNodeList oldChildren = projectData_Undo[0].SelectNodes($"//FM/Aspects/Aspect[@outputFn='{_selectedFn}' or @toFn='{_selectedFn}']")!;
                foreach (XmlNode oldChild in oldChildren)
                {
                    projectData_Undo[0].SelectSingleNode("//FM/Aspects")!.RemoveChild(oldChild);
                }
                _selectedFn = "-1";
            }
        }
        public void sFnIDName(string IDName)
        {
            updateUndo();
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/IDName")!.InnerText = IDName;
            selectedFunction!.IDName = IDName;
        }
        public void sFnDesc(string FnDesc)
        {
            updateUndo();
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/Description")!.InnerText = FnDesc;
            selectedFunction!.Description = FnDesc;
        }
        public void updateFnXY(double x, double y, string? sFn = null)
        {
            if (sFn == null)
            {
                sFn = _selectedFn;
            }
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={sFn}]/@x")!.InnerText = x.ToString("0.##", CultureInfo.InvariantCulture);
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={sFn}]/@y")!.InnerText = y.ToString("0.##", CultureInfo.InvariantCulture);
        }
        public void updateAspectXY(double x, double y, string directionX, string directionY)
        {
            projectData_Undo[0].SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@x")!.InnerText = x.ToString("0.##", CultureInfo.InvariantCulture);
            projectData_Undo[0].SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@y")!.InnerText = y.ToString("0.##", CultureInfo.InvariantCulture);
            projectData_Undo[0].SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@directionX")!.InnerText = directionX;
            projectData_Undo[0].SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{selectedLabel}\"]/@directionY")!.InnerText = directionY;
        }
        public void updateDisplaySetup(string index, string attr, string value)
        {
            if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}") == null)
            {
                projectData_Undo[0].SelectSingleNode("//FM/Functions")!.Attributes!.Append(projectData_Undo[0].CreateAttribute(attr));
            }
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}")!.Value = value;
            displaySetup[attr] = value;
            double dValue = double.NaN;
            double.TryParse(value, CultureInfo.InvariantCulture, out dValue);
            if (displaySetup[attr] == "")
            {
                displayValue[attr] = double.NaN;
            }
            else
            {
                displayValue[attr] = dValue;
            }
            checkDisplayValues(index);
        }
        public void checkDisplayValues(string index)
        {
            var minA = Math.Min(displayValue[$"colour_{index}4"], displayValue[$"colour_{index}3"]);
            minA = Math.Max(minA, displayValue[$"colour_{index}2"]);
            minA = Math.Max(minA, displayValue[$"colour_{index}1"]);
            if (minA is double.NaN) { minA = 0; }

            var maxA = Math.Max(displayValue[$"colour_{index}0"], displayValue[$"colour_{index}1"]);
            maxA = Math.Max(maxA, displayValue[$"colour_{index}2"]);
            maxA = Math.Max(maxA, displayValue[$"colour_{index}3"]);
            if (maxA is double.NaN) { maxA = 1; }

            if (displayValue[$"colour_{index}0"] is double.NaN) { displayValue[$"colour_{index}0"] = minA - 1.0; }
            if (displayValue[$"colour_{index}4"] is double.NaN) { displayValue[$"colour_{index}4"] = maxA * 1.0; }
            if (displayValue[$"colour_{index}2"] is double.NaN) { displayValue[$"colour_{index}2"] = (displayValue[$"colour_{index}0"] + displayValue[$"colour_{index}4"]) * 0.5; }
            if (displayValue[$"colour_{index}1"] is double.NaN) { displayValue[$"colour_{index}1"] = (displayValue[$"colour_{index}0"] + displayValue[$"colour_{index}2"]) * 0.5; }
            if (displayValue[$"colour_{index}3"] is double.NaN) { displayValue[$"colour_{index}3"] = (displayValue[$"colour_{index}2"] + displayValue[$"colour_{index}4"]) * 0.5; }
        }
        public void initialDisplaySetup(string attr, string value, bool overwrite = false)
        {
            if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}") == null)
            {
                projectData_Undo[0].SelectSingleNode("//FM/Functions")!.Attributes!.Append(projectData_Undo[0].CreateAttribute(attr));
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}")!.Value = value;
            }
            double dValue = double.NaN;
            if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}")!.Value is not null)
            {
                if (overwrite)
                {
                    projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}")!.Value = value;
                }
                displaySetup[attr] = projectData_Undo[0].SelectSingleNode($"//FM/Functions/@{attr}")!.Value!;
                double.TryParse(displaySetup[attr], CultureInfo.InvariantCulture, out dValue);
            }
            else
            {
                displaySetup[attr] = "";
            }
            if (displaySetup[attr] == "")
            {
                displayValue[attr] = double.NaN;
            }
            else
            {
                displayValue[attr] = dValue;
            }
        }
        public void updateKvalue(Function fn, string attr, string value)
        {
            if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={fn.IDNr}]/@{attr}") == null)
            {
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={fn.IDNr}]")!.Attributes!.Append(projectData_Undo[0].CreateAttribute(attr));
            }
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={fn.IDNr}]/@{attr}")!.Value = value;
            double tryValue = double.NaN;
            double.TryParse(value, CultureInfo.InvariantCulture, out tryValue);
            fn.Kvalues[attr] = tryValue;
        }
        public void updateColor(string selectedColor)
        {
            updateUndo();
            var setStyle = "custom";
            if (selectedColor == "")
            {
                setStyle = "";
            }
            else
            {
                selectedColor = uint.Parse(selectedColor.Replace("#", ""), NumberStyles.HexNumber).ToString();
            }
            foreach (var ID in selectedMulti)
            {
                if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={ID}]/@style") == null)
                {
                    XmlAttribute fnColorStyle = projectData_Undo[0].CreateAttribute("style");
                    projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={ID}]")!.Attributes!.Append(fnColorStyle);
                }
                if (projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={ID}]/@color") == null)
                {
                    XmlAttribute fnColorValue = projectData_Undo[0].CreateAttribute("color");
                    projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={ID}]")!.Attributes!.Append(fnColorValue);
                }
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={ID}]/@style")!.InnerText = setStyle;
                var setFn = functionList.Find(x => x.IDNr == ID);
                setFn!.fnColorStyle = setStyle;
                projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={ID}]/@color")!.InnerText = selectedColor;
                setFn!.fnColorValue = selectedColor;
            }
        }
        public void updateUndo()
        {
            if (projectData_Undo[0] is not null)
            {
                if (undoIndex > undoLength - 5)
                {
                    Array.Copy(projectData_Undo, moveBack + 1, projectData_Undo, 1, undoIndex - moveBack);
                    Array.Copy(selectedFn_Undo, moveBack + 1, selectedFn_Undo, 1, undoIndex - moveBack);
                    undoIndex -= moveBack;
                }
                undoIndex += 1;
                projectData_Undo[undoIndex] = (XmlDocument)projectData_Undo[0].Clone();
                selectedFn_Undo[undoIndex] = _selectedFn;
                projectData_Undo[undoIndex + 1] = new XmlDocument();
                projectData_Undo[undoIndex + 2] = new XmlDocument();
            }
        }
        public void reSetAspect(string[] dictArray, double xO, double yO, double aX, double aY, int aFW)
        {
            var aName = string.Join("|", dictArray);
            bool appendNew = false;
            double labelDx = 0;
            double labelDy = 0;
            string directionX = "from";
            string directionY = "to";
            string notGroup = "";
            XmlNode? aspectI = projectData_Undo[0].SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{aName}\"]");
            if (aspectI == null)
            {
                aspectI = projectData_Undo[0].CreateElement("Aspect");
                XmlAttribute aspectIx = projectData_Undo[0].CreateAttribute("x");
                aspectIx.Value = "0";
                aspectI.Attributes!.Append(aspectIx);
                XmlAttribute aspectIy = projectData_Undo[0].CreateAttribute("y");
                aspectIy.Value = "0";
                aspectI.Attributes.Append(aspectIy);
                XmlAttribute aspectIdx = projectData_Undo[0].CreateAttribute("directionX");
                aspectIdx.Value = "from";
                aspectI.Attributes.Append(aspectIdx);
                XmlAttribute aspectIdy = projectData_Undo[0].CreateAttribute("directionY");
                aspectIdy.Value = "to";
                aspectI.Attributes.Append(aspectIdy);
                XmlAttribute aspectIof = projectData_Undo[0].CreateAttribute("outputFn");
                aspectIof.Value = dictArray[0];
                aspectI.Attributes.Append(aspectIof);
                XmlAttribute aspectItf = projectData_Undo[0].CreateAttribute("toFn");
                aspectItf.Value = dictArray[2];
                aspectI.Attributes.Append(aspectItf);
                XmlAttribute addNotGroup = projectData_Undo[0].CreateAttribute("notGroup");
                addNotGroup.Value = "";
                aspectI.Attributes.Append(addNotGroup);
                XmlNode aspectIname = projectData_Undo[0].CreateElement("Name");
                aspectIname.InnerText = aName;
                aspectI.AppendChild(aspectIname);
                appendNew = true;
            }
            else
            {
                labelDx = 0;
                labelDy = 0;
                double.TryParse(aspectI.SelectSingleNode("@x")?.InnerText ?? "0", CultureInfo.InvariantCulture, out labelDx);
                double.TryParse(aspectI.SelectSingleNode("@y")?.InnerText ?? "0", CultureInfo.InvariantCulture, out labelDy);
                directionX = aspectI.SelectSingleNode("@directionX")?.InnerText ?? "from";
                directionY = aspectI.SelectSingleNode("@directionY")?.InnerText ?? "to";
                if (aspectI.SelectSingleNode("Curve") is not null)
                {
                    aspectI.RemoveChild(aspectI.SelectSingleNode("Curve")!);
                }
                if (aspectI.SelectSingleNode("Curve2") is not null)
                {
                    aspectI.RemoveChild(aspectI.SelectSingleNode("Curve2")!);
                }
                notGroup = aspectI.SelectSingleNode("@notGroup")?.InnerText ?? "";
                if (notGroup == "true") //This is added to open old files because of the slight repurposing of the notGroup attribute
                {
                    notGroup = "";
                    aspectI.SelectSingleNode("@notGroup")!.InnerText = "";
                }
            }
            XmlNode aspectIcurve = projectData_Undo[0].CreateElement("Curve");
            XmlNode aspectIcurve2 = projectData_Undo[0].CreateElement("Curve2");
            Coupling addCoupling = new(aName, labelDx, labelDy, directionX, directionY, notGroup, dictArray[0], dictArray[1], dictArray[2], dictArray[3],
                functionList.Find(x => x.IDNr == dictArray[0])!.x + xO, functionList.Find(x => x.IDNr == dictArray[0])!.y + yO, 0, 0);
            couplingList.Add(addCoupling);
            addCoupling.ReturnTextLines(aFW);
            aspectIcurve2.InnerText = addCoupling.reDrawLines(functionList.Find(x => x.IDNr == dictArray[2])!.x + aX,
                functionList.Find(x => x.IDNr == dictArray[2])!.y + aY, false);
            aspectIcurve.InnerText = addCoupling.curve;
            aspectI.AppendChild(aspectIcurve);
            aspectI.AppendChild(aspectIcurve2);
            if (appendNew) projectData_Undo[0].SelectSingleNode("//FM/Aspects")!.AppendChild(aspectI);
        }
        public void CreateMetadataTable(XmlNode fn)
        {
            if (!metadataDS.Tables.Contains("Metadata"))
            {
                DataTable metadataTable = new DataTable("Metadata");
                metadataTable.Columns.Add(new DataColumn("Cycle", typeof(int)));
                DataColumn colIdNr = new DataColumn("IDNr", typeof(int));
                metadataTable.Columns.Add(colIdNr);
                //metadataTable.PrimaryKey = new DataColumn[] { colIdNr };
                metadataTable.Columns.Add(new DataColumn("IDName", typeof(string)));
                metadataDS.Tables.Add(metadataTable);
            }
            foreach (XmlElement key in fn.SelectSingleNode("metadata")!)
            {
                if (!metadataDS.Tables["Metadata"]!.Columns.Contains(key.Name))
                {
                    metaDataKeys.Add(key.Name,"");
                    metadataDS.Tables["Metadata"]!.Columns.Add(new DataColumn(key.Name, typeof(string)));
                    metadataDS.Tables["Metadata"]!.Columns.Add(new DataColumn($"{key.Name}-active",typeof(string)));
                    metadataDS.Tables["Metadata"]!.Columns.Add(new DataColumn($"{key.Name}-equation", typeof(string)));
                }
            }
            var row = metadataDS.Tables["Metadata"]!.NewRow();
            row["Cycle"] = -1;
            row["IDNr"] = fn.SelectSingleNode("IDNr")!.InnerText;
            row["IDName"] = fn.SelectSingleNode("IDName")!.InnerText;
            foreach (XmlElement key in fn.SelectSingleNode("metadata")!)
            {
                row[key.Name] = key.InnerText;
                row[$"{key.Name}-active"] = key.Attributes["active"]?.Value ?? "";
                row[$"{key.Name}-equation"] = key.Attributes["equation"]?.Value ?? "";
            }
            metadataDS.Tables["Metadata"]!.Rows.Add(row);
        }
        public void updateMetadata (Function sFn)
        {
            var fnMD = projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={sFn.IDNr}]/metadata");
            if (fnMD is not null)
            {
                fnMD.RemoveAll();
                foreach (var keyValue in sFn.metadata)
                {
                    XmlNode addMeta = projectData_Undo[0].CreateElement(keyValue[0]);
                    addMeta.InnerText = keyValue[1];
                    XmlAttribute metaCheck = projectData_Undo[0].CreateAttribute("active");
                    metaCheck.Value = keyValue[2];
                    addMeta.Attributes!.Append(metaCheck);
                    XmlAttribute metaEquation = projectData_Undo[0].CreateAttribute("equation");
                    metaEquation.Value = keyValue[3];
                    addMeta.Attributes.Append(metaEquation);
                    fnMD.AppendChild(addMeta);
                }
            }
            metaIsDirty = false;
        }
        public void beginMultiSelect()
        {
            isSelecting = false;
            if (_selectedFn != "-1" && selectedFunction?.IDName == "")
            {
                defaultFnLabel();
            }
            if (selectedCoupling is not null)
            {
                selectedCoupling.aspectClass = "fn-point";
            }
            selectedFn = "-1";
            selectedLabel = "";
            touchAction = "auto";
        }
        public void groupFunctions()
        {
            if (selectedMulti.Count > 0)
            {
                foreach (var ID in selectedMulti)
                {

                }
            }
        }
        public void addMetadataResults(Function fnBG)
        {
            DataRow row = metadataDS.Tables["Metadata"]!.NewRow();
            foreach (DataColumn col in metadataDS.Tables["Metadata"]!.Columns)
            { //this section adds a new row to the Metadata table for the results of the evaluated Function (for the current Cycle)
                switch (col.ColumnName)
                {
                    case "Cycle": row[col] = cycleFMI; break;
                    case "IDNr": row[col] = fnBG.IDNr; break;
                    case "IDName": row[col] = fnBG.IDName; break;
                    default:
                        {
                            var evalRow = metadataDS.Tables["Metadata"]!.Select($"IDNr = '{fnBG.IDNr}' and Cycle = -1")[0];
                            if (evalRow is not null)
                            {
                                row[col] = evalRow[col];
                            }
                            break;
                        }
                }
            }
            metadataDS.Tables["Metadata"]!.Rows.Add(row);
        }
        public void calcVariables(Function iBG)
        {
            //iBG.metadata.Exists(key => key[2] == "true" && key[3].Contains(""))
            //Console.WriteLine($"===========================");
            //Console.WriteLine($"calcVariables_Function: {iBG.IDNr};{iBG.IDName}");
            iBG.newVariable("Cycle", new string[] { cycleFMI.ToString(), "The FMI Cycle count" });
            foreach (var mdKeyValue in metaDataKeys)
            {
                var mdKey = mdKeyValue.Key;
                var mdValue = mdKeyValue.Value;
                //GLOBAL!!!
                if (mdKey.StartsWith("Global_"))
                {
                    iBG.newVariable(mdKey, new string[] { mdValue, mdValue });
                }
                //Console.WriteLine($"  - key: {mdKey}");
                double Isum = 0;
                double In = 0;
                double Imin = double.NaN;
                double Imax = 0;
                double Iprd = double.NaN;
                double Psum = 0;
                double Pn = 0;
                double Pmin = double.NaN;
                double Pmax = 0;
                double Pprd = double.NaN;
                double Rsum = 0;
                double Rn = 0;
                double Rmin = double.NaN;
                double Rmax = 0;
                double Rprd = double.NaN;
                double Csum = 0;
                double Cn = 0;
                double Cmin = double.NaN;
                double Cmax = 0;
                double Cprd = double.NaN;
                double Tsum = 0;
                double Tn = 0;
                double Tmin = double.NaN;
                double Tmax = 0;
                double Tprd = double.NaN;
                double Amin = double.NaN;
                double Amax = double.NaN;
                double Aprd = double.NaN;
                foreach (var coupling in couplingList.FindAll(x => x.toFn == iBG.IDNr && x.fmiHighlight != "")) //activeAspectsArray.FindAll(x => x.toFn == iBG.IDNr)
                {   //we are only taking metadata from active couplings, that are joined to the aspects (not output) of the Function being evaluated
                    //However, we also need to inlcude couplings from Background Functions, that are always present but not flagged as 'active', so use the FMI highlighting.
                    //Console.WriteLine($"    - coupling: [{coupling.Name}]");
                    var outFn = functionList.Find(x => x.IDNr == coupling.outputFn);
                    var fnMD = outFn?.getVariable(mdKey);
                    if (fnMD is null && outFn?.metadata.Find(x => x[0] == mdKey) is not null)
                    { //how to handle empty aspect variables? Aggregate as zero or ignore?
                      //Console.WriteLine($"METADATA: {mdKey}");
                        fnMD = new string[] { "0", "0" }; //this adds missing aspects as zero data, if the upstream function has the corresponding Key name
                    }
                    if (fnMD is not null) // && !string.IsNullOrEmpty(fnMD[1])) 
                    {
                        //Console.WriteLine($"    - key-value: {fnMD[0]} | {fnMD[1]}");
                        var fnMDString = fnMD[0]; //fnMD[1]
                        double fnMDValue = 0;
                        if (!double.TryParse(string.IsNullOrEmpty(fnMDString) ? "0" : fnMDString, CultureInfo.InvariantCulture, out fnMDValue)) //how to handle empty aspect variables? Aggregate as zero or ignore?
                        {
                            if (fnMDString.ToLower() == "true") fnMDValue = 1;
                        }
                        switch (coupling.toType)
                        {
                            case "I":
                                {
                                    In++;
                                    Isum += fnMDValue;
                                    Imin = double.IsNaN(Imin) ? fnMDValue : Math.Min(Imin, fnMDValue);
                                    Imax = Math.Max(Imax, fnMDValue);
                                    Iprd = double.IsNaN(Iprd) ? fnMDValue : Iprd * fnMDValue;
                                    break;
                                }
                            case "P":
                                {
                                    Pn++;
                                    Psum += fnMDValue;
                                    Pmin = double.IsNaN(Pmin) ? fnMDValue : Math.Min(Pmin, fnMDValue);
                                    Pmax = Math.Max(Pmax, fnMDValue);
                                    Pprd = double.IsNaN(Pprd) ? fnMDValue : Pprd * fnMDValue;
                                    break;
                                }
                            case "R":
                                {
                                    Rn++;
                                    Rsum += fnMDValue;
                                    Rmin = double.IsNaN(Rmin) ? fnMDValue : Math.Min(Rmin, fnMDValue);
                                    Rmax = Math.Max(Rmax, fnMDValue);
                                    Rprd = double.IsNaN(Rprd) ? fnMDValue : Rprd * fnMDValue;
                                    break;
                                }
                            case "C":
                                {
                                    Cn++;
                                    Csum += fnMDValue;
                                    Cmin = double.IsNaN(Cmin) ? fnMDValue : Math.Min(Cmin, fnMDValue);
                                    Cmax = Math.Max(Cmax, fnMDValue);
                                    Cprd = double.IsNaN(Cprd) ? fnMDValue : Cprd * fnMDValue;
                                    break;
                                }
                            case "T":
                                {
                                    Tn++;
                                    Tsum += fnMDValue;
                                    Tmin = double.IsNaN(Tmin) ? fnMDValue : Math.Min(Tmin, fnMDValue);
                                    Tmax = Math.Max(Tmax, fnMDValue);
                                    Tprd = double.IsNaN(Tprd) ? fnMDValue : Tprd * fnMDValue;
                                    break;
                                }
                        }
                    }
                }
                if (In > 0)
                {
                    iBG.newVariable($"In_{mdKey}", new string[] { In.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Inputs" });
                    iBG.newVariable($"Isum_{mdKey}", new string[] { Isum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Inputs" });
                    iBG.newVariable($"Iavg_{mdKey}", new string[] { (Isum / In).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Inputs" });
                    iBG.newVariable($"Imin_{mdKey}", new string[] { Imin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Inputs" });
                    iBG.newVariable($"Imax_{mdKey}", new string[] { Imax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Inputs" });
                    iBG.newVariable($"Iprd_{mdKey}", new string[] { Iprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Inputs" });
                    Amin = Imin;
                    Amax = Imax;
                    Aprd = Iprd;
                }
                if (Pn > 0)
                {
                    iBG.newVariable($"Pn_{mdKey}", new string[] { Pn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Preconditions" });
                    iBG.newVariable($"Psum_{mdKey}", new string[] { Psum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Preconditions" });
                    iBG.newVariable($"Pavg_{mdKey}", new string[] { (Psum / Pn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Preconditions" });
                    iBG.newVariable($"Pmin_{mdKey}", new string[] { Pmin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Preconditions" });
                    iBG.newVariable($"Pmax_{mdKey}", new string[] { Pmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Preconditions" });
                    iBG.newVariable($"Pprd_{mdKey}", new string[] { Pprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Preconditions" });
                    Amin = double.IsNaN(Amin) ? Pmin : Math.Min(Amin, Pmin);
                    Amax = double.IsNaN(Amax) ? Pmax : Math.Max(Amax, Pmax);
                    Aprd = double.IsNaN(Aprd) ? Pprd : Aprd * Pprd;
                }
                if (Rn > 0)
                {
                    iBG.newVariable($"Rn_{mdKey}", new string[] { Rn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Resources" });
                    iBG.newVariable($"Rsum_{mdKey}", new string[] { Rsum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Resources" });
                    iBG.newVariable($"Ravg_{mdKey}", new string[] { (Rsum / Rn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Resources" });
                    iBG.newVariable($"Rmin_{mdKey}", new string[] { Rmin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Resources" });
                    iBG.newVariable($"Rmax_{mdKey}", new string[] { Rmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Resources" });
                    iBG.newVariable($"Rprd_{mdKey}", new string[] { Rprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Resources" });
                    Amin = double.IsNaN(Amin) ? Rmin : Math.Min(Amin, Rmin);
                    Amax = double.IsNaN(Amax) ? Rmax : Math.Max(Amax, Rmax);
                    Aprd = double.IsNaN(Aprd) ? Rprd : Aprd * Rprd;
                }
                if (Cn > 0)
                {
                    iBG.newVariable($"Cn_{mdKey}", new string[] { Cn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Controls" });
                    iBG.newVariable($"Csum_{mdKey}", new string[] { Csum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Controls" });
                    iBG.newVariable($"Cavg_{mdKey}", new string[] { (Csum / Cn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Controls" });
                    iBG.newVariable($"Cmin_{mdKey}", new string[] { Cmin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from Controls" });
                    iBG.newVariable($"Cmax_{mdKey}", new string[] { Cmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Controls" });
                    iBG.newVariable($"Cprd_{mdKey}", new string[] { Cprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Controls" });
                    Amin = double.IsNaN(Amin) ? Cmin : Math.Min(Amin, Cmin);
                    Amax = double.IsNaN(Amax) ? Cmax : Math.Max(Amax, Cmax);
                    Aprd = double.IsNaN(Aprd) ? Cprd : Aprd * Cprd;
                }
                if (Tn > 0)
                {
                    iBG.newVariable($"Tn_{mdKey}", new string[] { Tn.ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from Times" });
                    iBG.newVariable($"Tsum_{mdKey}", new string[] { Tsum.ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from Times" });
                    iBG.newVariable($"Tavg_{mdKey}", new string[] { (Tsum / Tn).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from Times" });
                    iBG.newVariable($"Tmin_{mdKey}", new string[] { Tmin.ToString(CultureInfo.InvariantCulture), "Minimum of {mdKey} from Times" });
                    iBG.newVariable($"Tmax_{mdKey}", new string[] { Tmax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from Times" });
                    iBG.newVariable($"Tprd_{mdKey}", new string[] { Tprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from Times" });
                    Amin = double.IsNaN(Amin) ? Tmin : Math.Min(Amin, Tmin);
                    Amax = double.IsNaN(Amax) ? Tmax : Math.Max(Amax, Tmax);
                    Aprd = double.IsNaN(Aprd) ? Tprd : Aprd * Tprd;
                }
                if (In + Pn + Rn + Cn + Tn > 0)
                {
                    iBG.newVariable($"An_{mdKey}", new string[] { (In + Pn + Rn + Cn + Tn).ToString(CultureInfo.InvariantCulture), $"Number of {mdKey} from All Aspects" });
                    iBG.newVariable($"Asum_{mdKey}", new string[] { (Isum + Psum + Rsum + Csum + Tsum).ToString(CultureInfo.InvariantCulture), $"Sum of {mdKey} from All Aspects" });
                    iBG.newVariable($"Aavg_{mdKey}", new string[] { ((Isum + Psum + Rsum + Csum + Tsum) / (In + Pn + Rn + Cn + Tn)).ToString(CultureInfo.InvariantCulture), $"Average of {mdKey} from All Aspects" });
                    iBG.newVariable($"Amin_{mdKey}", new string[] { Amin.ToString(CultureInfo.InvariantCulture), $"Minimum of {mdKey} from All Aspects" });
                    iBG.newVariable($"Amax_{mdKey}", new string[] { Amax.ToString(CultureInfo.InvariantCulture), $"Maximum of {mdKey} from All Aspects" });
                    iBG.newVariable($"Aprd_{mdKey}", new string[] { Aprd.ToString(CultureInfo.InvariantCulture), $"Product of {mdKey} from All Aspects" });
                }
                // if (mdKey == "Effectiveness") //Just used for troubleshooting
                // {
                //     Console.WriteLine($"  - An_{mdKey}: {(In + Pn + Rn + Cn + Tn)}");
                //     Console.WriteLine($"  - Asum_{mdKey}: {(Isum + Psum + Rsum + Csum + Tsum)}");
                //     Console.WriteLine($"  - Aavg_{mdKey}: {(Isum + Psum + Rsum + Csum + Tsum) / (In + Pn + Rn + Cn + Tn)}");
                //     Console.WriteLine($"  - Amin_{mdKey}: {Amin}");
                //     Console.WriteLine($"  - Amax_{mdKey}: {Amax}");
                //     Console.WriteLine($"  - Aprd_{mdKey}: {Aprd}");
                // }
            }
        }
        public string eval(string str, Dictionary<string, string[]> variables)
        {
            int pos = -1;
            char ch;
            void nextChar()
            {
                ch = (++pos < str.Length) ? str[pos] : '`';
            }
            bool eat(char charToEat)
            {
                while (char.IsWhiteSpace(ch)) nextChar();
                if (ch == charToEat)
                {
                    nextChar();
                    return true;
                }
                return false;
            }
            string parse()
            {
                nextChar();
                string x = parseExpression();
                //Console.WriteLine($"str: {str}; Length: {str.Length}; pos: {pos}");
                //Console.WriteLine($"x: {x}");
                if (pos < str.Length)
                {
                    return "NaN";
                }
                return x;
            }
            // Grammar:
            // expression = term | expression `+` term | expression `-` term
            // term = factor | term `*` factor | term `/` factor
            // factor = `+` factor | `-` factor | `(` expression `)`
            //
            string parseExpression()
            {
                string x = parseTerm();
                for (; ; )
                {
                    if (eat('+'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) + double.Parse(parseTerm(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); // addition
                    }
                    else if (eat('-'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) - double.Parse(parseTerm(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); // subtraction
                    }
                    else if (eat('?'))
                    { // conditionals. Grammer: if ? then : else | [if?then:else]
                        if (double.Parse(x, CultureInfo.InvariantCulture) > 0)
                        {
                            str = str.Substring(pos, str.IndexOf(':') - pos);
                        }
                        else
                        {
                            str = str.Substring(str.IndexOf(':') + 1);
                        }
                        pos = -1;
                        nextChar();
                        if (eat('"'))
                        {
                            int startPos = pos;
                            while (ch != '"' && pos < str.Length) nextChar();
                            string subText = str.Substring(startPos, pos - startPos);
                            pos = str.Length;
                            return subText;
                        }
                        return parseExpression();
                    }
                    else if (eat('&'))
                    { // boolean AND
                        eat('&');
                        bool tA = double.Parse(parseTerm(), CultureInfo.InvariantCulture) > 0;
                        x = double.Parse(x, CultureInfo.InvariantCulture) > 0 && tA ? "1" : "0";
                        //return x;
                    }
                    else if (eat('|'))
                    { // boolean OR
                        eat('|');
                        bool tO = double.Parse(parseTerm(), CultureInfo.InvariantCulture) > 0;
                        x = double.Parse(x, CultureInfo.InvariantCulture) > 0 || tO ? "1" : "0";
                        //return x;
                    }
                    else return x;
                }
            }
            string parseTerm()
            {
                string x = parseFactor();
                for (; ; )
                {
                    if (eat('*'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) * double.Parse(parseFactor(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); // multiplication
                    }
                    else if (eat('/'))
                    {
                        x = (double.Parse(x, CultureInfo.InvariantCulture) / double.Parse(parseFactor(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); // division
                    }
                    else return x;
                }
            }
            string parseFactor()
            {
                if (eat('+')) return parseFactor(); // unary plus
                if (eat('-')) return '-' + parseFactor(); // unary minus
                if (eat('!')) return double.Parse(parseFactor(), CultureInfo.InvariantCulture) > 0 ? "0" : "1"; // unary NOT
                string x;
                int startPos = pos;
                if (eat('"'))
                {
                    startPos = pos;
                    while (ch != '"' && pos < str.Length) nextChar();
                    string subText = str.Substring(startPos, pos - startPos);
                    x = subText;
                    eat('"');
                }
                else if (eat('('))
                { // parentheses
                    x = parseExpression();
                    eat(')');
                }
                else if (eat('π'))
                {
                    x = Math.PI.ToString(CultureInfo.InvariantCulture); //Pi
                }
                else if ((ch >= '0' && ch <= '9') || ch == '.')
                { // numbers
                    while ((ch >= '0' && ch <= '9') || ch == '.') nextChar();
                    x = str.Substring(startPos, pos - startPos);
                }
                else if (ch >= 'a' && ch <= 'z')
                { // functions (must start with lowercase letter)
                    while ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_') nextChar();
                    string func = str.Substring(startPos, pos - startPos);
                    if (func == "lookup")
                    {
                        if (dataRows.Count <= 0) return "NaN";
                        if (!eat('(')) return "NaN";
                        int row = int.Parse(parseExpression());
                        //Console.WriteLine($" - row: {row}");
                        if (!eat(',')) return "NaN";
                        int col = int.Parse(parseExpression());
                        //Console.WriteLine($" - col: {col}");
                        if (!eat(')')) return "NaN";
                        if (row >= dataRows.Count) return "NaN";
                        if (col >= dataRows[row].Length) return "NaN";
                        x = dataRows[row][col];
                    }
                    else if (func == "xref")
                    {
                        if (dataKeys.Count <= 0) return "NaN";
                        if (!eat('(')) return "NaN";
                        string key = parseExpression();
                        if (!eat(',')) return "NaN";
                        string value = parseExpression();
                        if (!eat(')')) return "NaN";
                        key = $"{key},{value}";
                        if (!dataKeys.ContainsKey(key)) return "NaN";
                        x = dataKeys[key];
                    }
                    else if (func.StartsWith("rnorm"))
                    {
                        if (!eat('(')) return "NaN";
                        double mu = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (!eat(',')) return "NaN";
                        double sigma = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (!eat(')')) return "NaN";
                        var epsilon = Double.MinValue;
                        var u = rnd.NextDouble();
                        while (u <= epsilon)
                        {
                            u = rnd.NextDouble();
                        }
                        var v = rnd.NextDouble();
                        var rnorm = sigma * (Math.Sqrt(-2.0 * Math.Log(u)) * Math.Sin(2.0 * Math.PI * v)) + mu;
                        if (func == "rnormi")
                        {
                            x = Convert.ToInt32(rnorm).ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            x = rnorm.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    else if (func == "rnd")
                    {
                        if (!eat('(')) return "NaN";
                        double rangeFrom = 0.0;
                        double rangeTo = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (eat(','))
                        {
                            rangeFrom = rangeTo;
                            rangeTo = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        }
                        if (!eat(')')) return "NaN";
                        var epsilon = Double.MinValue;
                        var u = rnd.NextDouble();
                        while (u <= epsilon)
                        {
                            u = rnd.NextDouble();
                        }
                        if (double.IsNaN(rangeFrom) || double.IsNaN(rangeTo))
                        {
                            x = u.ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            x = (u * (rangeTo - rangeFrom) + rangeFrom).ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    else if (func == "rndi")
                    {
                        if (!eat('(')) return "NaN";
                        int rangeFrom = 0;
                        int rangeTo = 2;
                        string exp = parseExpression();
                        int.TryParse(exp == "NaN" ? "2" : exp, CultureInfo.InvariantCulture, out rangeTo);
                        if (eat(','))
                        {
                            rangeFrom = 0;
                            int.TryParse(exp == "NaN" ? "0" : exp, CultureInfo.InvariantCulture, out rangeFrom);
                            rangeTo = 2;
                            exp = parseExpression();
                            int.TryParse(exp == "NaN" ? "2" : exp, CultureInfo.InvariantCulture, out rangeTo);
                        }
                        if (!eat(')')) return "NaN";
                        x = rnd.Next(rangeFrom, rangeTo).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "min")
                    {
                        if (!eat('(')) return "NaN";
                        double a = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (!eat(',')) return "NaN";
                        double b = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (!eat(')')) return "NaN";
                        x = Math.Min(a, b).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "max")
                    {
                        if (!eat('(')) return "NaN";
                        double a = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (!eat(',')) return "NaN";
                        double b = double.Parse(parseExpression(), CultureInfo.InvariantCulture);
                        if (!eat(')')) return "NaN";
                        x = Math.Max(a, b).ToString(CultureInfo.InvariantCulture);
                    }
                    else if (func == "api")
                    {
                        return "NaN"; //api function should be handled at the higher evaluate() level
                    }
                    else if (func == "round")
                    {
                        if (!eat('(')) return "NaN";
                        x = parseExpression();
                        if (!eat(','))
                        {
                            x = Math.Round(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            double f = Math.Pow(10, double.Parse(parseExpression(), CultureInfo.InvariantCulture));
                            x = (Math.Round(double.Parse(x, CultureInfo.InvariantCulture) * f) / f).ToString(CultureInfo.InvariantCulture);
                        }
                        if (!eat(')')) return "NaN";
                    }
                    else
                    {
                        x = parseFactor();
                        if (func == "sqrt") x = Math.Sqrt(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        else if (func == "sin") x = Math.Sin(double.Parse(x, CultureInfo.InvariantCulture) * Math.PI / 180).ToString(CultureInfo.InvariantCulture);
                        else if (func == "cos") x = Math.Cos(double.Parse(x, CultureInfo.InvariantCulture) * Math.PI / 180).ToString(CultureInfo.InvariantCulture);
                        else if (func == "tan") x = Math.Tan(double.Parse(x, CultureInfo.InvariantCulture) * Math.PI / 180).ToString(CultureInfo.InvariantCulture);
                        else if (func == "abs") x = Math.Abs(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        else if (func == "exp") x = Math.Exp(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        else if (func == "log") x = Math.Log(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        //else if (func == "round") x = Math.Round(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        else if (func == "ceiling") x = Math.Ceiling(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        else if (func == "floor") x = Math.Floor(double.Parse(x, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                        else if (func == "int") x = x.Split(".")[0]; // int.Parse(x, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                        else x = func;
                    }
                }
                else if (ch >= 'A' && ch <= 'Z')
                { // variables - must start with Capital Letter
                    while ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || ch == '_') nextChar();
                    string variable = str.Substring(startPos, pos - startPos);
                    x = variables.ContainsKey(variable) ? variables[variable][0] : "0";
                    if (string.IsNullOrEmpty(x)) x = "0";
                }
                else
                {
                    return "NaN";
                }
                if (eat('^')) x = Math.Pow(double.Parse(x, CultureInfo.InvariantCulture), double.Parse(parseFactor(), CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture); // exponentiation
                else if (eat('π')) x = (double.Parse(x, CultureInfo.InvariantCulture) * Math.PI).ToString(CultureInfo.InvariantCulture); // if Pi follows a number directly
                else if (eat('='))
                { // comparison operations
                    eat('=');
                    double isNumberE;
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out isNumberE))
                    {
                        x = isNumberE == double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                    }
                    else
                    {
                        x = x == parseFactor() ? "1" : "0";
                    }
                }
                else if (eat('<'))
                {
                    double isNumberL;
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out isNumberL))
                    {
                        if (eat('=')) x = isNumberL <= double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        else if (eat('>')) x = isNumberL != double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        else x = isNumberL < double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                    }
                    else
                    {
                        int compareL = x.CompareTo(parseFactor());
                        if (eat('=')) x = compareL <= 0 ? "1" : "0";
                        else if (eat('>')) x = compareL != 0 ? "1" : "0";
                        else x = compareL < 0 ? "1" : "0";
                    }
                }
                else if (eat('>'))
                {
                    double isNumberG;
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out isNumberG))
                    {
                        if (eat('<'))
                        {
                            bool tX = double.Parse(parseFactor(), CultureInfo.InvariantCulture) > 0;
                            x = (isNumberG > 0 || tX) && !(isNumberG > 0 && tX) ? "1" : "0";
                        }
                        else if (eat('=')) x = isNumberG >= double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                        else x = isNumberG > double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                    }
                    else
                    {
                        int compareG = x.CompareTo(parseFactor());
                        if (eat('=')) x = compareG >= 0 ? "1" : "0";
                        else x = compareG > 0 ? "1" : "0";
                    }
                }
                else if (eat('!') && eat('='))
                {
                    double isNumberN;
                    if (double.TryParse(x, CultureInfo.InvariantCulture, out isNumberN))
                    {
                        x = isNumberN != double.Parse(parseFactor(), CultureInfo.InvariantCulture) ? "1" : "0";
                    }
                    else
                    {
                        int compareN = x.CompareTo(parseFactor());
                        x = compareN != 0 ? "1" : "0";
                    }
                }
                return x == null ? "NaN" : x;
            }
            return parse();
        }
        // inject HttpClient Http
        // private async Task<string> requestAPI(HttpRequestMessage requestMessage)
        // {
        //     var reply = await Http.SendAsync(requestMessage);
        //     return await reply.Content.ReadAsStringAsync();
        // }
    }
}
