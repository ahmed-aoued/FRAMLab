using System.Text.Json.Serialization;

namespace FRAMLab.Shared.MainApp.Models
{
    public record Position(double X, double Y, double Width, double Height);

    public record CouplingGeometry(
        double StartX,
        double StartY,
        double ControlAX,
        double ControlAY,
        double ControlBX,
        double ControlBY,
        double EndX,
        double EndY);

    public class LayoutFunctionDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public string FnStyle { get; set; } = "";
    }

    public class LayoutAspectDto
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string FunctionId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Orphan { get; set; } = "";
    }

    public class LayoutCouplingDto
    {
        public string Name { get; set; } = "";
        public string OutputFn { get; set; } = "";
        public string ToFn { get; set; } = "";
        public string Label { get; set; } = "";
        public double LabelDx { get; set; }
        public double LabelDy { get; set; }
        public double DrawFromX { get; set; }
        public double DrawFromY { get; set; }
        public double DrawToX { get; set; }
        public double DrawToY { get; set; }
        public double DrawAX { get; set; }
        public double DrawAY { get; set; }
        public double DrawBX { get; set; }
        public double DrawBY { get; set; }
        public double DrawIntX { get; set; }
        public double DrawIntY { get; set; }
    }

    public class LayoutSnapshot
    {
        public const int LatestVersion = 1;
        public int Version { get; set; } = LatestVersion;
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;
        public double ViewWidth { get; set; }
        public double ViewHeight { get; set; }
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }
        public List<LayoutFunctionDto> Functions { get; set; } = new();
        public List<LayoutAspectDto> Aspects { get; set; } = new();
        public List<LayoutCouplingDto> Couplings { get; set; } = new();
        public Dictionary<string, Position> FunctionPositions { get; set; } = new();
        public Dictionary<string, CouplingGeometry> CouplingGeometries { get; set; } = new();
    }

    public record TableRowLayout(
        string LevelId,
        string LevelName,
        double LabelX,
        double LabelY,
        double Width,
        double Height,
        string FillColor);

    public record TableColumnLayout(
        string AgentId,
        string AgentName,
        double LabelX,
        double LabelY,
        double Width,
        double Height);

    public record TableCellLayout(
        string LevelId,
        string AgentId,
        double X,
        double Y,
        double Width,
        double Height,
        string FillColor,
        bool ShouldMeasure);

    public record TableCellMeasurement(string LevelId, string AgentId, double Width, double Height);
}
