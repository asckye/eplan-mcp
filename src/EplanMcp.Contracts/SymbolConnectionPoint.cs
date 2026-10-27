namespace EplanMcp.Contracts;

/// <summary>Geometry of a library variant's connection point (api-notes A52–A55).</summary>
public sealed class SymbolConnectionPoint
{
    /// <summary>Symbol-local [x, y] location in millimetres, before placement.</summary>
    public double[] Location { get; set; } = [];
    /// <summary>Native PinBase.Directions name.</summary>
    public string Direction { get; set; } = string.Empty;
}
