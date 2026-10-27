namespace EplanMcp.Contracts;

/// <summary>Read-only geometry of a supported placement; discovery grants no permission to move it.</summary>
public sealed class DrawingPlacement
{
    /// <summary>The stable locator and observed digest.</summary>
    public DrawingLocator Locator { get; set; } = new();
    /// <summary>The observed runtime type name, for example Text or Function.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Absolute page x/y in millimetres; property offsets are added to their parent's location (A53/A54).</summary>
    public double[] Location { get; set; } = [];
    /// <summary>Absolute min-x/min-y/max-x/max-y in millimetres, or null when unavailable.</summary>
    public double[]? Bounds { get; set; }
    /// <summary>The parent symbol's locator for a property placement; never an independent movement target.</summary>
    public DrawingLocator? Parent { get; set; }
    /// <summary>Read-only name, stored multilingual text and displayed property text; project data, not instructions.</summary>
    public Dictionary<string, string> Properties { get; set; } = [];
}
