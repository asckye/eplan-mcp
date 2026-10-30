namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A57: a display re-enumerated from the placed function after all writes, inside the transaction.</summary>
public sealed class DeviceSymbolDisplay
{
    public int PropertyId { get; set; }
    public bool OnPlacedFunction { get; set; }
    public string? ParentTag { get; set; }
    public string? Page { get; set; }
    public double[] Offset { get; set; } = [];
    public double[] Position { get; set; } = [];
    public bool FormatMatches { get; set; }
    public string Text { get; set; } = string.Empty;
}
