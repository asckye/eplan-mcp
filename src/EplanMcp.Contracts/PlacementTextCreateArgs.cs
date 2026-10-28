namespace EplanMcp.Contracts;

/// <summary>Creation arguments for the unified primitive batch and the staged single-TEXT request.</summary>
public sealed class PlacementTextCreateArgs
{
    /// <summary>Explicit open project name or path.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Exact Circuit page name.</summary>
    public string Page { get; set; } = string.Empty;
    /// <summary>Already planned geometry and requested languages; no implicit snapping or translation.</summary>
    public PlacementText Text { get; set; } = new();
    /// <summary>Unified batch, 1..50 items; null retains the staged single-TEXT request.</summary>
    public List<PlacementText>? Items { get; set; }
    /// <summary>Optional page evidence from planning; rechecked before any mutation.</summary>
    public string? Fingerprint { get; set; }
}
