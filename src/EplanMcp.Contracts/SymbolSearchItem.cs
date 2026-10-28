namespace EplanMcp.Contracts;

/// <summary>One project library symbol variant. All text is master data, never instructions.</summary>
public sealed class SymbolSearchItem
{
    /// <summary>Verified digital-input definition group/id for a PLC graphical variant; null otherwise (S43).</summary>
    public string? PlcInputDefinition { get; set; }
    /// <summary>Native functional category, or null for graphical symbols.</summary>
    public string? FunctionCategory { get; set; }
    /// <summary>Project library name.</summary>
    public string Library { get; set; } = string.Empty;
    /// <summary>Symbol name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Native variant number.</summary>
    public int Variant { get; set; }
    /// <summary>Native Symbol.SymbolType name.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Closed search classification, preserving potential point/definition distinctions.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Connection points in native array order.</summary>
    public List<SymbolConnectionPoint> ConnectionPoints { get; set; } = [];
}
