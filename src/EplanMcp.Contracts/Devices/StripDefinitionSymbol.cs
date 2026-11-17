namespace EplanMcp.Contracts;

/// <summary>Host-resolved internal selection for a new placed terminal strip definition (A83).</summary>
public sealed class StripDefinitionSymbol
{
    /// <summary>Only the project SPECIAL library is supported.</summary>
    public string Library { get; set; } = "SPECIAL";
    /// <summary>Exact symbol name in the SPECIAL library.</summary>
    public string Symbol { get; set; } = "TDEF";
    /// <summary>Native symbol variant number.</summary>
    public int Variant { get; set; }
    /// <summary>Exact plugin versions that supplied the effective values; part of the plan fingerprint.</summary>
    public string[] Plugins { get; set; } = [];
}
