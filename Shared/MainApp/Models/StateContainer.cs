using FMV_OG.Shared.MainApp.Services;
using System.Data;
using System.Globalization;
using System.Xml;

namespace FMV_OG.Shared
{
    public class StateContainer
    {
        private readonly IExpressionEvaluator expressionEvaluator;
        private readonly IFMIDisplayConfigService fmiDisplayConfigService;
        private readonly IMetadataService metadataService;
        private readonly IUndoService undoService;
        private readonly AgentLevelService agentLevelService;
        private bool _agentsLevelsViewActive;
        private readonly IProjectDocumentService projectDocumentService;

        public StateContainer(
            IExpressionEvaluator expressionEvaluator,
            IFMIDisplayConfigService fmiDisplayConfigService,
            IMetadataService metadataService,
            IUndoService undoService,
            IProjectDocumentService projectDocumentService,
            AgentLevelService agentLevelService)
        {
            this.expressionEvaluator = expressionEvaluator;
            this.fmiDisplayConfigService = fmiDisplayConfigService;
            this.metadataService = metadataService;
            this.undoService = undoService;
            this.projectDocumentService = projectDocumentService;
            this.agentLevelService = agentLevelService;
            this.agentLevelService.Bind(this);
        }

        //Done
        private string _selectedFn = "-1";
        //Done
        private string _selectedLabel = "";
        
        public bool showProperties = false;
        public bool showAspects = false;
        public bool showFMIProfile = false;
        public bool showMetadata = false;
        public string rightPanel { get; set; } = "";
        public string activeMode = "Basic";
        public bool showMetaDetail = false; //If false, this hides the -active and -equation columns in the Metadata table view - This data is now displayed in the Tooltip, so keep false
        public string showKeys = "";
        public string lastKey = "";
        public int cycleFMI = -1;
        public Function? selectedFunction;
        public Coupling? selectedCoupling;
        public DataSet metadataDS
        {
            get => metadataService.MetadataDS;
            set => metadataService.MetadataDS = value;
        }
        public Dictionary<string, string> metaDataKeys
        {
            get => metadataService.MetaDataKeys;
            set => metadataService.MetaDataKeys = value;
        }
        public bool metaIsDirty
        {
            get => metadataService.MetaIsDirty;
            set => metadataService.MetaIsDirty = value;
        }
        //Done
        public string fnClass { get; set; } = "fn-hover";
        public List<string[]> dataRows { get; set; } = new List<string[]>();
        public Dictionary<string, string> dataKeys { get; set; } = new Dictionary<string, string>();
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
        public bool AgentsLevelsViewActive
        {
            get => _agentsLevelsViewActive;
            set => _agentsLevelsViewActive = value;
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
        public XmlDocument[] projectData_Undo
        {
            get => undoService.ProjectDataUndo;
            set => undoService.ProjectDataUndo = value;
        }
        public int undoLength => undoService.UndoLength;
        public XmlNode? fnGroups { get; set; }
        public string[] selectedFn_Undo
        {
            get => undoService.SelectedFnUndo;
            set => undoService.SelectedFnUndo = value;
        }
        public int undoIndex
        {
            get => undoService.UndoIndex;
            set => undoService.UndoIndex = value;
        }
        public List<Coupling> couplingList { get; set; } = new List<Coupling>();
        public List<Function> functionList { get; set; } = new List<Function>();
        public string fnName { get; set; } = "";
        public bool isDisabled { get; set; } = true;
        public List<Aspect> aspectsList { get; set; } = new List<Aspect>();
        public List<Aspect> outputsList { get; set; } = new List<Aspect>();
        public List<Agent> agentList { get; set; } = new List<Agent>();
        public List<Level> levelList { get; set; } = new List<Level>();
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
        public Dictionary<string, string> displaySetup => fmiDisplayConfigService.DisplaySetup;
        public Dictionary<string, double> displayValue => fmiDisplayConfigService.DisplayValue;
        public bool hideFMIhighlight { get; set; } = false;
        public bool hideFMIdisplaytext { get; set; } = false;
        public List<string> typeFMIdisplay 
        {
            get 
            {
                return fmiDisplayConfigService.TypeFMIDisplay;
            } 
            set
            {
                fmiDisplayConfigService.SetTypeFMIDisplay(projectData_Undo[0], value);
            }
        }
        public void defaultFnAttributes(XmlNode fn)
        {
            projectDocumentService.DefaultFnAttributes(projectData_Undo[0], fn);
        }
        public void defaultFnLabel()
        {
            projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={_selectedFn}]/IDName")!.InnerText = _selectedFn;
            selectedFunction!.IDName = _selectedFn;
            fnName = _selectedFn;
        }
        public void sFnIsInput(string isInput)
        {
            projectDocumentService.SetFunctionIsInput(projectData_Undo[0], _selectedFn, selectedFunction, isInput);
        }
        public void sFnOrphans(int orphans)
        {
            projectDocumentService.SetFunctionOrphans(projectData_Undo[0], _selectedFn, selectedFunction, orphans);
        }
        public void sFnFunctionType()
        {
            projectDocumentService.SetFunctionTypeByConnections(projectData_Undo[0], selectedFn, selectedFunction);
        }
        public void setFunctionType(string functionId, string functionType, Function? function = null)
        {
            projectDocumentService.SetFunctionType(projectData_Undo[0], functionId, functionType, function);
        }
        public void deleteFn()
        {
            if (projectDocumentService.DeleteFunction(this))
            {
                _selectedFn = "-1";
            }
        }
        public void sFnIDName(string IDName)
        {
            projectDocumentService.SetFunctionIdName(this, IDName);
        }
        public void sFnDesc(string FnDesc)
        {
            projectDocumentService.SetFunctionDescription(this, FnDesc);
        }
        public void updateFnXY(double x, double y, string? sFn = null)
        {
            projectDocumentService.UpdateFunctionXY(projectData_Undo[0], _selectedFn, x, y, sFn);
        }
        public void ToggleAgentsLevelsView()
        {
            if (_agentsLevelsViewActive)
            {
                agentLevelService.RestoreAgentsLevelsView();
            }
            else
            {
                agentLevelService.ApplyAgentsLevelsView();
            }
        }
        public void updateAspectXY(double x, double y, string directionX, string directionY)
        {
            projectDocumentService.UpdateAspectXY(projectData_Undo[0], selectedLabel, x, y, directionX, directionY);
        }
        public void updateDisplaySetup(string index, string attr, string value)
        {
            fmiDisplayConfigService.UpdateDisplaySetup(projectData_Undo[0], index, attr, value);
        }
        public void checkDisplayValues(string index)
        {
            fmiDisplayConfigService.CheckDisplayValues(index);
        }
        public void initialDisplaySetup(string attr, string value, bool overwrite = false)
        {
            fmiDisplayConfigService.InitialDisplaySetup(projectData_Undo[0], attr, value, overwrite);
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
            undoService.UpdateUndo(_selectedFn);
        }
        public void reSetAspect(string[] dictArray, double xO, double yO, double aX, double aY, int aFW)
        {
            projectDocumentService.ResetAspect(this, dictArray, xO, yO, aX, aY, aFW);
        }
        public void CreateMetadataTable(XmlNode fn)
        {
            metadataService.CreateMetadataTable(fn);
        }
        public void updateMetadata (Function sFn)
        {
            metadataService.UpdateMetadata(projectData_Undo[0], sFn);
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
            metadataService.AddMetadataResults(fnBG, cycleFMI);
        }
        public void calcVariables(Function iBG)
        {
            metadataService.CalcVariables(iBG, couplingList, functionList, cycleFMI);
        }
        public string eval(string str, Dictionary<string, string[]> variables)
        {
            return expressionEvaluator.Eval(str, variables, dataRows, dataKeys);
        }
        // ── Agents ──────────────────────────────────────────────
        public XmlNode EnsureAgentsRoot()
        {
            if (projectData_Undo[0].SelectSingleNode("//FM/Agents") is not XmlNode root)
            {
                root = projectData_Undo[0].CreateElement("Agents");
                projectData_Undo[0].SelectSingleNode("//FM")!.AppendChild(root);
            }
            return root;
        }

        public Agent CreateAgent(string name, string agentType = "", string color = "")
        {
            updateUndo();
            var agentsRoot = EnsureAgentsRoot();
            int nextId = NextId("//FM/Agents/Agent/IDNr");
            var node = projectData_Undo[0].CreateElement("Agent");
            AppendChildElement(node, "IDNr", nextId.ToString());
            AppendChildElement(node, "Name", name);
            AppendChildElement(node, "AgentType", agentType);
            AppendChildElement(node, "Color", color);
            agentsRoot.AppendChild(node);
            var agent = new Agent(node);
            agentList.Add(agent);
            return agent;
        }

        public void UpdateAgentColor(Agent agent, string color)
        {
            updateUndo();
            agent.Color = color;
            var node = projectData_Undo[0].SelectSingleNode($"//FM/Agents/Agent[IDNr=\"{agent.IDNr}\"]");
            if (node is null) return;
            var colorNode = node.SelectSingleNode("Color");
            if (colorNode is null)
            {
                AppendChildElement(node, "Color", color);
            }
            else
            {
                colorNode.InnerText = color;
            }
        }

        public void RemoveAgent(string agentId)
        {
            updateUndo();
            var node = projectData_Undo[0].SelectSingleNode($"//FM/Agents/Agent[IDNr=\"{agentId}\"]");
            node?.ParentNode?.RemoveChild(node);
            agentList.RemoveAll(a => a.IDNr == agentId);
            // clear from any assigned functions
            foreach (var fn in functionList.Where(f => f.AgentId == agentId))
            {
                fn.AgentId = "";
                SyncFunctionAttribute(fn, "agentId", "");
            }
        }

        public void AssignAgentToFunction(Function function, string agentId)
        {
            updateUndo();
            function.AgentId = agentId;
            SyncFunctionAttribute(function, "agentId", agentId);
        }

        public void ClearAgentFromFunction(Function function)
        {
            updateUndo();
            function.AgentId = "";
            SyncFunctionAttribute(function, "agentId", "");
        }

        // ── Levels ─────────────────────────────────────────────
        public XmlNode EnsureLevelsRoot()
        {
            if (projectData_Undo[0].SelectSingleNode("//FM/Levels") is not XmlNode root)
            {
                root = projectData_Undo[0].CreateElement("Levels");
                projectData_Undo[0].SelectSingleNode("//FM")!.AppendChild(root);
            }
            return root;
        }

        public Level CreateLevel(string name, int hierarchy = 0, string color = "")
        {
            updateUndo();
            var levelsRoot = EnsureLevelsRoot();
            int nextId = NextId("//FM/Levels/Level/IDNr");
            var node = projectData_Undo[0].CreateElement("Level");
            if (!string.IsNullOrWhiteSpace(color))
            {
                var colorAttribute = projectData_Undo[0].CreateAttribute("color");
                colorAttribute.Value = color;
                node.Attributes!.Append(colorAttribute);
            }
            AppendChildElement(node, "IDNr", nextId.ToString());
            AppendChildElement(node, "Name", name);
            AppendChildElement(node, "Hierarchy", hierarchy.ToString());
            levelsRoot.AppendChild(node);
            var level = new Level(node);
            levelList.Add(level);
            return level;
        }

        public void UpdateLevelColor(Level level, string color)
        {
            updateUndo();
            level.Color = color;
            var node = projectData_Undo[0].SelectSingleNode($"//FM/Levels/Level[IDNr=\"{level.IDNr}\"]");
            if (node is null) return;
            var colorAttribute = node.Attributes?["color"];
            if (colorAttribute is null)
            {
                colorAttribute = projectData_Undo[0].CreateAttribute("color");
                node.Attributes!.Append(colorAttribute);
            }
            colorAttribute.Value = color;
            agentLevelService.RefreshAgentsLevelsLayout();
        }

        public void RemoveLevel(string levelId)
        {
            updateUndo();
            var node = projectData_Undo[0].SelectSingleNode($"//FM/Levels/Level[IDNr=\"{levelId}\"]");
            node?.ParentNode?.RemoveChild(node);
            levelList.RemoveAll(l => l.IDNr == levelId);
            foreach (var fn in functionList.Where(f => f.LevelId == levelId))
            {
                fn.LevelId = "";
                SyncFunctionAttribute(fn, "levelId", "");
            }
        }

        public void AssignLevelToFunction(Function function, string levelId)
        {
            updateUndo();
            function.LevelId = levelId;
            SyncFunctionAttribute(function, "levelId", levelId);
        }

        public void ClearLevelFromFunction(Function function)
        {
            updateUndo();
            function.LevelId = "";
            SyncFunctionAttribute(function, "levelId", "");
        }


        

        // ── Shared XML helpers ─────────────────────────────────
        private void SyncFunctionAttribute(Function function, string attrName, string value)
        {
            var fnNode = projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={function.IDNr}]");
            if (fnNode is null) return;
            if (fnNode.Attributes![attrName] is null)
            {
                fnNode.Attributes.Append(projectData_Undo[0].CreateAttribute(attrName));
            }
            fnNode.Attributes[attrName]!.Value = value;
        }

        private int NextId(string xpath)
        {
            int next = 0;
            var nodes = projectData_Undo[0].SelectNodes(xpath);
            if (nodes is not null)
            {
                foreach (XmlNode n in nodes)
                {
                    if (int.TryParse(n.InnerText, out var v))
                        next = Math.Max(next, v + 1);
                }
            }
            return next;
        }

        private void AppendChildElement(XmlNode parent, string name, string value)
        {
            var el = projectData_Undo[0].CreateElement(name);
            el.InnerText = value;
            parent.AppendChild(el);
        }
    }
}
