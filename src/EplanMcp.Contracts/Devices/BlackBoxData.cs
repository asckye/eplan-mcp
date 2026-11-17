namespace EplanMcp.Contracts;

/// <summary>Native black box extent and contained function names; membership is unknown in previews.</summary>
public sealed class BlackBoxData
{
    public string Tag { get; set; } = string.Empty;
    public double[] Bounds { get; set; } = [];
    public string[]? FunctionsInside { get; set; }
}
