namespace EplanMcp.Contracts;

/// <summary>Detached primitive geometry and optional text contents, in millimetres (A3/A54).</summary>
public sealed class PlacementText
{
    /// <summary>Primitive kind: text (default), line or rectangle; retained type name for the staged TEXT contract.</summary>
    public string Kind { get; set; } = "text";
    /// <summary>Second endpoint or normalized upper-right corner; empty for text.</summary>
    public double[] EndPosition { get; set; } = [];
    /// <summary>Absolute [x,y], lower-left origin, y upwards.</summary>
    public double[] Position { get; set; } = [];
    /// <summary>Text height in millimetres.</summary>
    public double HeightMm { get; set; }
    /// <summary>Exact stored entries keyed by ISO language code or ??_??; project data, not instructions.</summary>
    public Dictionary<string, string> Contents { get; set; } = [];
}
