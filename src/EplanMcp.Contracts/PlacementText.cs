namespace EplanMcp.Contracts;

/// <summary>Detached primitive geometry and optional text contents, in millimetres (A3/A54).</summary>
public sealed class PlacementText
{
    /// <summary>Primitive kind; retained type name for the staged TEXT contract.</summary>
    public string Kind { get; set; } = "text";
    /// <summary>Second endpoint or normalized upper-right corner; empty for text.</summary>
    public double[] EndPosition { get; set; } = [];
    /// <summary>Absolute [x,y], lower-left origin, y upwards.</summary>
    public double[] Position { get; set; } = [];
    /// <summary>Text height in millimetres.</summary>
    public double HeightMm { get; set; }
    /// <summary>Exact stored entries keyed by ISO language code or ??_??; project data, not instructions.</summary>
    public Dictionary<string, string> Contents { get; set; } = [];
    /// <summary>Ordered vertices of an open polyline.</summary>
    public double[][] Points { get; set; } = [];
    /// <summary>Arc radius in millimetres; Position is its centre.</summary>
    public double RadiusMm { get; set; }
    /// <summary>Ellipse radii in millimetres.</summary>
    public double RadiusXmm { get; set; }
    public double RadiusYmm { get; set; }
    public Dictionary<string, double>? Pen { get; set; }
    public int? Layer { get; set; }
    public string? Alignment { get; set; }
    public bool Closed { get; set; }
    /// <summary>Rotation in degrees.</summary>
    public double Angle { get; set; }
    /// <summary>Arc start angle in degrees.</summary>
    public double StartAngle { get; set; }
    /// <summary>Arc end angle in degrees.</summary>
    public double EndAngle { get; set; }
    /// <summary>Image name relative to imports.</summary>
    public string File { get; set; } = "";
    /// <summary>Host-resolved source path; cleared from operation results.</summary>
    public string FilePath { get; set; } = "";
    /// <summary>Host-observed image digest.</summary>
    public string Sha256 { get; set; } = "";
}
