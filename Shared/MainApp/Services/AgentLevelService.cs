using System.Globalization;
using System.Xml;
using FMV_OG.Shared;
using FMV_OG.Shared.MainApp.Models;
using FMV_OG.Shared.MainApp.Services.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FMV_OG.Shared.MainApp.Services
{
    public class AgentLevelService
    {
        private const double DefaultTablePadding = 24;
        private const double DefaultRowHeaderWidth = 160;
        private const double DefaultColumnHeaderHeight = 70;
        private const double DefaultTableCellPadding = 16;
        private const double DefaultFunctionSlotWidth = 100;
        private const double DefaultFunctionSlotHeight = 100;
        private const double DefaultFunctionSlotSpacing = 12;

        private readonly IConfiguration renderConfig;
        private readonly ILogger<AgentLevelService> logger;
        private readonly List<TableRowLayout> _tableRows = new();
        private readonly List<TableColumnLayout> _tableColumns = new();
        private readonly List<TableCellLayout> _tableCells = new();
        private readonly Dictionary<(string levelId, string agentId), List<Function>> _tableCellFunctions = new();
        private StateContainer? _stateContainer;
        private LayoutSnapshot? _agentsLevelsBackup;
        private string _measurementLevelId = string.Empty;
        private string _measurementAgentId = string.Empty;
        private List<string>? _agentsLevelsOriginalOrder;
        private int _tableCellColumnCapacity = 1;
        private int _tableCellRowCapacity = 1;
        private Dictionary<string, (string levelId, string agentId)> _lastFunctionAssignments = new();
        private int _lastAgentCount;
        private int _lastLevelCount;

        public AgentLevelService(IConfiguration renderConfig, ILogger<AgentLevelService> logger)
        {
            this.renderConfig = renderConfig;
            this.logger = logger;
        }

        public void Bind(StateContainer stateContainer)
        {
            _stateContainer = stateContainer;
        }

        public IReadOnlyList<TableRowLayout> TableRows => _tableRows;
        public IReadOnlyList<TableColumnLayout> TableColumns => _tableColumns;
        public IReadOnlyList<TableCellLayout> TableCells => _tableCells;
        public IReadOnlyDictionary<(string levelId, string agentId), List<Function>> TableCellFunctions => _tableCellFunctions;

        public bool RefreshLayoutIfChanged()
        {
            var state = EnsureState();
            if (!state.AgentsLevelsViewActive) return false;

            bool changed = state.functionList.Count != _lastFunctionAssignments.Count ||
                           state.agentList.Count != _lastAgentCount ||
                           state.levelList.Count != _lastLevelCount;

            if (!changed)
            {
                foreach (var fn in state.functionList)
                {
                    if (!_lastFunctionAssignments.TryGetValue(fn.IDNr, out var assignment) ||
                        assignment.levelId != NormalizeLevelKey(fn.LevelId) ||
                        assignment.agentId != NormalizeAgentKey(fn.AgentId))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                RebuildAgentsLevelsTableLayout();
                return true;
            }
            return false;
        }

        public void RefreshAgentsLevelsLayout()
        {
            var state = EnsureState();
            if (!state.AgentsLevelsViewActive) return;
            RebuildAgentsLevelsTableLayout();
        }

        public double TablePadding => DefaultTablePadding;
        public double RowHeaderWidth => DefaultRowHeaderWidth;
        public double ColumnHeaderHeight => DefaultColumnHeaderHeight;
        public double TableCellPadding => DefaultTableCellPadding;
        public double FunctionSlotWidth => DefaultFunctionSlotWidth;
        public double FunctionSlotHeight => DefaultFunctionSlotHeight;
        public double FunctionSlotSpacing => DefaultFunctionSlotSpacing;
        public double TableCellWidth { get; private set; } = 280;
        public double TableCellHeight { get; private set; } = 220;
        public double MeasuredLargestCellWidth { get; private set; }
        public double MeasuredLargestCellHeight { get; private set; }
        public Dictionary<string, Position> FunctionPositions { get; private set; } = new();
        public Dictionary<string, CouplingGeometry> CouplingGeometries { get; private set; } = new();

        public IReadOnlyList<Function> GetFunctionsForCell(string levelId, string agentId)
        {
            levelId ??= string.Empty;
            agentId ??= string.Empty;
            if (_tableCellFunctions.TryGetValue((levelId, agentId), out var list))
            {
                return list;
            }
            return Array.Empty<Function>();
        }


        public void ApplyAgentsLevelsView()
        {
            var state = EnsureState();
            if (!state.fileLoaded || state.projectData_Undo[0] is null || state.functionList.Count == 0)
            {
                logger.LogWarning(LogEvents.AgentsLevels, "Agents/Levels view cannot be enabled - no project is loaded.");
                return;
            }
            _agentsLevelsBackup = BuildLayoutSnapshot();
            _agentsLevelsOriginalOrder = state.functionList.Select(fn => fn.IDNr).ToList();
            state.AgentsLevelsViewActive = true;
            logger.LogInformation(LogEvents.AgentsLevels, "Agents/Levels view enabled. functions={FunctionCount}", state.functionList.Count);
            ApplyAgentsLevelsLayout();
        }

        public void RestoreAgentsLevelsView()
        {
            var state = EnsureState();
            var snapshot = _agentsLevelsBackup;
            var order = _agentsLevelsOriginalOrder;
            if (snapshot is null)
            {
                logger.LogWarning(LogEvents.AgentsLevels, "Restore requested but no layout backup exists.");
                state.debugOutput = "No layout backup found to restore.";
                return;
            }

            ApplyLayoutSnapshot(snapshot!);
            if (order is not null)
            {
                var lookup = state.functionList.ToDictionary(f => f.IDNr);
                var restored = new List<Function>();
                var orderSet = new HashSet<string>(order);
                foreach (var id in order)
                {
                    if (lookup.TryGetValue(id, out var fn))
                    {
                        restored.Add(fn);
                    }
                }
                restored.AddRange(state.functionList.Where(fn => !orderSet.Contains(fn.IDNr)));
                state.functionList = restored;
            }

            FunctionPositions = new Dictionary<string, Position>();
            CouplingGeometries = new Dictionary<string, CouplingGeometry>();
            ResetAgentsLevelsViewState();
            logger.LogInformation(LogEvents.AgentsLevels, "Agents/Levels view disabled.");
        }

        public void ResetAgentsLevelsViewState()
        {
            var state = EnsureState();
            _agentsLevelsBackup = null;
            _agentsLevelsOriginalOrder = null;
            state.AgentsLevelsViewActive = false;
            _tableRows.Clear();
            _tableColumns.Clear();
            _tableCells.Clear();
            _tableCellFunctions.Clear();
            TableCellWidth = 280;
            TableCellHeight = 220;
            _measurementLevelId = string.Empty;
            _measurementAgentId = string.Empty;
            MeasuredLargestCellWidth = 0;
            MeasuredLargestCellHeight = 0;
            logger.LogInformation(LogEvents.AgentsLevels, "Agents/Levels view state reset to defaults.");
        }

        public void UpdateTableCellMeasurement(TableCellMeasurement measurement)
        {
            if (measurement.LevelId != _measurementLevelId || measurement.AgentId != _measurementAgentId)
            {
                return;
            }
            MeasuredLargestCellWidth = measurement.Width;
            MeasuredLargestCellHeight = measurement.Height;
        }

        private void ApplyAgentsLevelsLayout()
        {
            RebuildAgentsLevelsTableLayout();
        }

        private StateContainer EnsureState()
        {
            return _stateContainer ?? throw new InvalidOperationException("AgentLevelService is not bound to StateContainer.");
        }

        private void ApplyLayoutSnapshot(LayoutSnapshot snapshot)
        {
            var state = EnsureState();
            state.viewWidth = snapshot.ViewWidth;
            state.viewHeight = snapshot.ViewHeight;
            state.canvasWidth = snapshot.CanvasWidth;
            state.canvasHeight = snapshot.CanvasHeight;

            foreach (var dto in snapshot.Couplings)
            {
                var coupling = state.couplingList.FirstOrDefault(c => c.Name == dto.Name);
                if (coupling is null) continue;

                coupling.drawFromx = dto.DrawFromX;
                coupling.drawFromy = dto.DrawFromY;
                coupling.drawTox = dto.DrawToX;
                coupling.drawToy = dto.DrawToY;
                coupling.drawAx = dto.DrawAX;
                coupling.drawAy = dto.DrawAY;
                coupling.drawBx = dto.DrawBX;
                coupling.drawBy = dto.DrawBY;
                coupling.drawIntx = dto.DrawIntX;
                coupling.drawInty = dto.DrawIntY;

                coupling.labelDx = dto.LabelDx;
                coupling.labelDy = dto.LabelDy;
                coupling.resetLabel();
            }

            foreach (var dto in snapshot.Functions)
            {
                var fn = state.functionList.Find(x => x.IDNr == dto.Id);
                if (fn is null)
                {
                    continue;
                }
                fn.x = dto.X;
                fn.y = dto.Y;
                fn.fnStyle = dto.FnStyle;
                fn.IDName = dto.Name;
                UpdateFunctionNode(dto);
            }

            foreach (var dto in snapshot.Aspects)
            {
                var aspect = state.aspectsList.Find(x => x.Type == dto.Type && x.IDNr == dto.Id);
                if (aspect is null)
                {
                    continue;
                }
                aspect.IDName = dto.Name;
                aspect.FunctionIDNr = dto.FunctionId;
                aspect.Orphan = dto.Orphan;
            }
        }

        private LayoutSnapshot BuildLayoutSnapshot()
        {
            var state = EnsureState();
            return new LayoutSnapshot
            {
                Version = LayoutSnapshot.LatestVersion,
                SavedAt = DateTime.UtcNow,
                ViewWidth = state.viewWidth,
                ViewHeight = state.viewHeight,
                CanvasWidth = state.canvasWidth,
                CanvasHeight = state.canvasHeight,
                Functions = state.functionList.Select(fn => new LayoutFunctionDto
                {
                    Id = fn.IDNr,
                    Name = fn.IDName,
                    X = fn.x,
                    Y = fn.y,
                    FnStyle = fn.fnStyle
                }).ToList(),
                Aspects = state.aspectsList.Select(a => new LayoutAspectDto
                {
                    Id = a.IDNr,
                    Type = a.Type,
                    FunctionId = a.FunctionIDNr,
                    Name = a.IDName,
                    Orphan = a.Orphan
                }).ToList(),
                Couplings = state.couplingList.Select(c => new LayoutCouplingDto
                {
                    Name = c.Name,
                    OutputFn = c.outputFn,
                    ToFn = c.toFn,
                    Label = c.label,
                    LabelDx = c.labelDx,
                    LabelDy = c.labelDy,
                    DrawFromX = c.drawFromx,
                    DrawFromY = c.drawFromy,
                    DrawToX = c.drawTox,
                    DrawToY = c.drawToy,
                    DrawAX = c.drawAx,
                    DrawAY = c.drawAy,
                    DrawBX = c.drawBx,
                    DrawBY = c.drawBy,
                    DrawIntX = c.drawIntx,
                    DrawIntY = c.drawInty
                }).ToList(),
                FunctionPositions = new Dictionary<string, Position>(FunctionPositions),
                CouplingGeometries = new Dictionary<string, CouplingGeometry>(CouplingGeometries)
            };
        }

        private void RebuildAgentsLevelsTableLayout()
        {
            var state = EnsureState();
            _tableRows.Clear();
            _tableColumns.Clear();
            _tableCells.Clear();
            _tableCellFunctions.Clear();
            _lastFunctionAssignments.Clear();
            _lastAgentCount = state.agentList.Count;
            _lastLevelCount = state.levelList.Count;

            var levelLookup = state.levelList.ToDictionary(l => l.IDNr);
            var agentLookup = state.agentList.ToDictionary(a => a.IDNr);

            var hasFunctionsWithoutLevel = state.functionList.Any(fn => string.IsNullOrWhiteSpace(GetPrimaryLevelKey(fn, levelLookup)));
            var hasFunctionsWithoutAgent = state.functionList.Any(fn => string.IsNullOrWhiteSpace(GetPrimaryAgentKey(fn, agentLookup)));

            var levelKeys = BuildOrderedLevelKeys(levelLookup, hasFunctionsWithoutLevel);
            if (levelKeys.Count == 0)
            {
                levelKeys.Add(string.Empty);
            }
            var agentKeys = BuildOrderedAgentKeys(agentLookup, hasFunctionsWithoutAgent);
            if (agentKeys.Count == 0)
            {
                agentKeys.Add(string.Empty);
            }

            var orderedFunctions = state.functionList
                .OrderBy(fn => GetLevelSortTuple(fn, levelLookup))
                .ThenBy(fn => GetAgentSortTuple(fn, agentLookup))
                .ThenBy(fn => fn.IDName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(fn => fn.IDNr, StringComparer.OrdinalIgnoreCase)
                .ToList();
            state.functionList = orderedFunctions;

            var maxFunctionsInCell = 0;
            _measurementLevelId = levelKeys[0];
            _measurementAgentId = agentKeys[0];

            foreach (var function in orderedFunctions)
            {
                var levelKey = NormalizeLevelKey(GetPrimaryLevelKey(function, levelLookup));
                var agentKey = NormalizeAgentKey(GetPrimaryAgentKey(function, agentLookup));
                var key = (levelKey, agentKey);
                _lastFunctionAssignments[function.IDNr] = (levelKey, agentKey);
                if (!_tableCellFunctions.TryGetValue(key, out var bucket))
                {
                    bucket = new List<Function>();
                    _tableCellFunctions[key] = bucket;
                }
                bucket.Add(function);
                if (bucket.Count > maxFunctionsInCell)
                {
                    maxFunctionsInCell = bucket.Count;
                    _measurementLevelId = levelKey;
                    _measurementAgentId = agentKey;
                }
            }

            ComputeTableCellDimensions(maxFunctionsInCell);
            MeasuredLargestCellWidth = 0;
            MeasuredLargestCellHeight = 0;

            var tableWidth = DefaultTablePadding * 2 + DefaultRowHeaderWidth + agentKeys.Count * TableCellWidth;
            var tableHeight = DefaultTablePadding * 2 + DefaultColumnHeaderHeight + levelKeys.Count * TableCellHeight;
            state.viewWidth = Math.Max(tableWidth, 600);
            state.viewHeight = Math.Max(tableHeight, 600);
            state.canvasWidth = state.viewWidth * state.scaleZoom;
            state.canvasHeight = state.viewHeight * state.scaleZoom;

            for (var rowIndex = 0; rowIndex < levelKeys.Count; rowIndex++)
            {
                var levelId = levelKeys[rowIndex];
                levelLookup.TryGetValue(levelId, out var level);
                var levelName = ResolveLevelName(levelId, level);
                var fillColor = ResolveLevelColor(level);
                var y = DefaultTablePadding + DefaultColumnHeaderHeight + rowIndex * TableCellHeight;
                var labelY = y + TableCellHeight / 2;
                _tableRows.Add(new TableRowLayout(
                    levelId,
                    levelName,
                    DefaultTablePadding + DefaultRowHeaderWidth - 12,
                    labelY,
                    agentKeys.Count * TableCellWidth,
                    TableCellHeight,
                    fillColor));
            }

            for (var columnIndex = 0; columnIndex < agentKeys.Count; columnIndex++)
            {
                var agentId = agentKeys[columnIndex];
                agentLookup.TryGetValue(agentId, out var agent);
                var agentName = ResolveAgentName(agentId, agent);
                var x = DefaultTablePadding + DefaultRowHeaderWidth + columnIndex * TableCellWidth;
                var labelX = x + TableCellWidth / 2;
                var labelY = DefaultTablePadding + DefaultColumnHeaderHeight / 2;
                _tableColumns.Add(new TableColumnLayout(
                    agentId,
                    agentName,
                    labelX,
                    labelY,
                    TableCellWidth,
                    TableCellHeight));
            }

            for (var rowIndex = 0; rowIndex < levelKeys.Count; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < agentKeys.Count; columnIndex++)
                {
                    var levelId = levelKeys[rowIndex];
                    var agentId = agentKeys[columnIndex];
                    var x = DefaultTablePadding + DefaultRowHeaderWidth + columnIndex * TableCellWidth;
                    var y = DefaultTablePadding + DefaultColumnHeaderHeight + rowIndex * TableCellHeight;
                    var fillColor = ResolveLevelColor(levelLookup.GetValueOrDefault(levelId));
                    var shouldMeasure = levelId == _measurementLevelId && agentId == _measurementAgentId;
                    _tableCells.Add(new TableCellLayout(
                        levelId,
                        agentId,
                        x,
                        y,
                        TableCellWidth,
                        TableCellHeight,
                        fillColor,
                        shouldMeasure));
                    if (!_tableCellFunctions.ContainsKey((levelId, agentId)))
                    {
                        _tableCellFunctions[(levelId, agentId)] = new List<Function>();
                    }
                }
            }

            foreach (var cell in _tableCells)
            {
                if (!_tableCellFunctions.TryGetValue((cell.LevelId, cell.AgentId), out var functionsInCell))
                {
                    continue;
                }
                PositionFunctionsWithinCell(cell, functionsInCell);
            }

            RefreshCouplingsForLayout();
            FunctionPositions = new Dictionary<string, Position>();
            CouplingGeometries = new Dictionary<string, CouplingGeometry>();
        }

        private void UpdateFunctionNode(LayoutFunctionDto dto)
        {
            var state = EnsureState();
            var fnNode = state.projectData_Undo[0].SelectSingleNode($"//FM/Functions/Function[IDNr={dto.Id}]");
            if (fnNode is null)
            {
                return;
            }
            var xAttr = fnNode.Attributes?["x"] ?? state.projectData_Undo[0].CreateAttribute("x");
            var yAttr = fnNode.Attributes?["y"] ?? state.projectData_Undo[0].CreateAttribute("y");
            var styleAttr = fnNode.Attributes?["fnStyle"] ?? state.projectData_Undo[0].CreateAttribute("fnStyle");
            xAttr.Value = dto.X.ToString("0.##", CultureInfo.InvariantCulture);
            yAttr.Value = dto.Y.ToString("0.##", CultureInfo.InvariantCulture);
            styleAttr.Value = dto.FnStyle ?? "";
            if (fnNode.Attributes?["x"] is null) fnNode.Attributes?.Append(xAttr);
            if (fnNode.Attributes?["y"] is null) fnNode.Attributes?.Append(yAttr);
            if (fnNode.Attributes?["fnStyle"] is null) fnNode.Attributes?.Append(styleAttr);
            var nameNode = fnNode.SelectSingleNode("IDName");
            if (nameNode is not null)
            {
                nameNode.InnerText = dto.Name ?? "";
            }
        }

        private List<string> BuildOrderedLevelKeys(IReadOnlyDictionary<string, Level> levelLookup, bool includeUnassigned)
        {
            var ordered = levelLookup.Values
                .OrderBy(l => l.Hierarchy)
                .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .Select(l => l.IDNr)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            if (includeUnassigned)
            {
                ordered.Add(string.Empty);
            }
            return ordered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<string> BuildOrderedAgentKeys(IReadOnlyDictionary<string, Agent> agentLookup, bool includeUnassigned)
        {
            var ordered = agentLookup.Values
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .Select(a => a.IDNr)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            if (includeUnassigned)
            {
                ordered.Add(string.Empty);
            }
            return ordered.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private (int hierarchy, string name) GetLevelSortTuple(Function fn, IReadOnlyDictionary<string, Level> levelLookup)
        {
            if (!string.IsNullOrEmpty(fn.LevelId) && levelLookup.TryGetValue(fn.LevelId, out var level))
            {
                return (level.Hierarchy, level.Name ?? string.Empty);
            }
            return (int.MaxValue, string.Empty);
        }

        private string GetAgentSortTuple(Function fn, IReadOnlyDictionary<string, Agent> agentLookup)
        {
            if (!string.IsNullOrEmpty(fn.AgentId) && agentLookup.TryGetValue(fn.AgentId, out var agent))
            {
                return agent.Name ?? string.Empty;
            }
            return "\uffff";
        }

        private string GetPrimaryLevelKey(Function function, IReadOnlyDictionary<string, Level> levelLookup)
        {
            if (!string.IsNullOrWhiteSpace(function.LevelId) && levelLookup.ContainsKey(function.LevelId))
            {
                return function.LevelId;
            }
            return string.Empty;
        }

        private string GetPrimaryAgentKey(Function function, IReadOnlyDictionary<string, Agent> agentLookup)
        {
            if (!string.IsNullOrWhiteSpace(function.AgentId) && agentLookup.ContainsKey(function.AgentId))
            {
                return function.AgentId;
            }
            return string.Empty;
        }

        private string NormalizeLevelKey(string? levelId)
        {
            return string.IsNullOrWhiteSpace(levelId) ? string.Empty : levelId;
        }

        private string NormalizeAgentKey(string? agentId)
        {
            return string.IsNullOrWhiteSpace(agentId) ? string.Empty : agentId;
        }

        private string ResolveLevelName(string levelId, Level? level)
        {
            if (string.IsNullOrWhiteSpace(levelId))
            {
                return "No Level";
            }
            return level?.Name ?? levelId;
        }

        private string ResolveAgentName(string agentId, Agent? agent)
        {
            if (string.IsNullOrWhiteSpace(agentId))
            {
                return "No Agent";
            }
            return agent?.Name ?? agentId;
        }

        private string ResolveLevelColor(Level? level)
        {
            if (level is null || string.IsNullOrWhiteSpace(level.Color))
            {
                return "#F3F3F3";
            }
            return level.Color;
        }

        private void ComputeTableCellDimensions(int maxFunctionsInCell)
        {
            if (maxFunctionsInCell <= 0)
            {
                _tableCellColumnCapacity = 1;
                _tableCellRowCapacity = 1;
                TableCellWidth = 280;
                TableCellHeight = 220;
                return;
            }

            _tableCellColumnCapacity = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(maxFunctionsInCell)));
            _tableCellRowCapacity = Math.Max(1, (int)Math.Ceiling(maxFunctionsInCell / (double)_tableCellColumnCapacity));

            var contentWidth = DefaultTableCellPadding * 2
                + _tableCellColumnCapacity * DefaultFunctionSlotWidth
                + (_tableCellColumnCapacity - 1) * DefaultFunctionSlotSpacing;
            var contentHeight = DefaultTableCellPadding * 2
                + _tableCellRowCapacity * DefaultFunctionSlotHeight
                + (_tableCellRowCapacity - 1) * DefaultFunctionSlotSpacing;

            TableCellWidth = Math.Max(280, contentWidth);
            TableCellHeight = Math.Max(220, contentHeight);
        }

        private void PositionFunctionsWithinCell(TableCellLayout cell, List<Function> functions)
        {
            var state = EnsureState();
            if (functions.Count == 0)
            {
                return;
            }

            int columns = Math.Max(1, _tableCellColumnCapacity);
            var startX = cell.X + DefaultTableCellPadding;
            var startY = cell.Y + DefaultTableCellPadding;

            for (var index = 0; index < functions.Count; index++)
            {
                var column = index % columns;
                var row = index / columns;
                var fn = functions[index];
                fn.x = startX + column * (DefaultFunctionSlotWidth + DefaultFunctionSlotSpacing);
                fn.y = startY + row * (DefaultFunctionSlotHeight + DefaultFunctionSlotSpacing);
                state.updateFnXY(fn.x, fn.y, fn.IDNr);
            }
        }

        private void RefreshCouplingsForLayout()
        {
            var state = EnsureState();
            if (state.projectData_Undo[0] is null || state.couplingList.Count == 0)
            {
                return;
            }
            var fnLookup = state.functionList.ToDictionary(fn => fn.IDNr);
            foreach (var coupling in state.couplingList)
            {
                if (!fnLookup.TryGetValue(coupling.outputFn, out var outputFn) ||
                    !fnLookup.TryGetValue(coupling.toFn, out var targetFn))
                {
                    continue;
                }
                var (outX, outY) = GetAspectCoordinates(outputFn, "O");
                var (targetX, targetY) = GetAspectCoordinates(targetFn, coupling.toType);
                coupling.reDrawLines(outX, outY, true);
                coupling.reDrawLines(targetX, targetY, false);
                coupling.resetLabel();

                var aspectNode = state.projectData_Undo[0].SelectSingleNode($"//FM/Aspects/Aspect[Name=\"{coupling.Name}\"]");
                if (aspectNode is not null)
                {
                    var curveNode = aspectNode.SelectSingleNode("Curve");
                    var curve2Node = aspectNode.SelectSingleNode("Curve2");
                    if (curveNode is not null)
                    {
                        curveNode.InnerText = coupling.curve;
                    }
                    if (curve2Node is not null)
                    {
                        curve2Node.InnerText = coupling.curve2;
                    }
                }
            }
        }

        private (double x, double y) GetAspectCoordinates(Function function, string aspectType)
        {
            var xKey = $"{function.fnStyle}:aspectX:{aspectType}";
            var yKey = $"{function.fnStyle}:aspectY:{aspectType}";
            var xOffset = ParseConfigDouble(renderConfig[xKey], 50);
            var yOffset = ParseConfigDouble(renderConfig[yKey], 50);
            return (function.x + xOffset, function.y + yOffset);
        }

        private static double ParseConfigDouble(string? value, double fallback)
        {
            if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
            return fallback;
        }
    }
}
