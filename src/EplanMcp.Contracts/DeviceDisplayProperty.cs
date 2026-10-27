namespace EplanMcp.Contracts;

/// <summary>A requested display property and its symbol-relative offset in millimetres (A53/A54).</summary>
public sealed class DeviceDisplayProperty
{
    /// <summary>One of the closed display property IDs supported by device placement.</summary>
    public int PropertyId { get; set; }
    /// <summary>Horizontal offset from the symbol, not a page coordinate.</summary>
    public double OffsetXmm { get; set; }
    /// <summary>Vertical offset from the symbol, not a page coordinate.</summary>
    public double OffsetYmm { get; set; }
}
