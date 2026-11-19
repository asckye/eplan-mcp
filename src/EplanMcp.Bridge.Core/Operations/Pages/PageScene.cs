using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// One page as the planners, the layout guard and the page review see it: the drawing with its placements, the connection
/// points, the drawn and the logical connections, and what could not be read. Read once per operation and passed on;
/// the connection, placement, device and review operations all predict and check against it (A52).
/// </summary>
public sealed class PageScene
{
    public OpenProject Project { get; set; } = new();
    public PageDrawingGeometry Page { get; set; } = new();
    public string PageType { get; set; } = "Circuit";
    public bool Complete { get; set; }
    public Dictionary<string, SymbolConnectionPoint> Pins { get; set; } = [];
    public Dictionary<string, List<double[][]>> PointRoutes { get; set; } = [];
    public Dictionary<string, string> PointRouteGaps { get; set; } = [];
    public List<double[][]> Graphical { get; set; } = [];
    public List<ConnectionRecord> Logical { get; set; } = [];
    public Dictionary<string, double[]> SymbolBounds { get; set; } = [];
    public LayoutGuard.Options Layout { get; set; } = new();
    [Newtonsoft.Json.JsonIgnore]
    public HashSet<string> ConfigLayoutMembers { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal PageScene WithLayout(LayoutGuard.Options layout)
    {
        var scene = (PageScene)MemberwiseClone();
        scene.Layout = layout;
        return scene;
    }
    // A reader, not scene data: plans that serialize the scene (placement move) must not follow its closure back here.
    [Newtonsoft.Json.JsonIgnore]
    public Func<CableDefinitionPlanner.Inventory>? ReadCables { get; set; }
}
