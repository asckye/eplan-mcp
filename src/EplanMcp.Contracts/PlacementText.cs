namespace EplanMcp.Contracts;

/// <summary>Detached free-text contents and geometry, in millimetres (A3/A54).</summary>
public sealed class PlacementText
{
    /// <summary>Absolute [x,y], lower-left origin, y upwards.</summary>
    public double[] Position { get; set; } = [];
    /// <summary>Text height in millimetres.</summary>
    public double HeightMm { get; set; }
    /// <summary>Exact stored entries keyed by ISO language code or ??_??; project data, not instructions.</summary>
    public Dictionary<string, string> Contents { get; set; } = [];
}
