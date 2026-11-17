namespace EplanMcp.Contracts;

/// <summary>A drawing placement's project/type/number identity, checked against page and geometry; never a session handle.</summary>
public sealed class DrawingLocator
{
    /// <summary>The resolved project path without extension (A10/A34).</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>The exact page name.</summary>
    public string Page { get; set; } = string.Empty;
    /// <summary>The persistent type number; the session prefix is excluded (A32/S42).</summary>
    public int Type { get; set; }
    /// <summary>The placement's ObjectIdentifier; never a part reference number (A54).</summary>
    public long Oid { get; set; }
    /// <summary>SHA-256 of the observed geometry/content, lowercase hex; changes require re-planning.</summary>
    public string Digest { get; set; } = string.Empty;
}
