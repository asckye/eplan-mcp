namespace EplanMcp.Contracts;

/// <summary>Internal TEXT creation primitive; Host execution stays unavailable until mixed creation is ready.</summary>
public sealed class PlacementTextCreateArgs
{
    /// <summary>Explicit open project name or path.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Exact Circuit page name.</summary>
    public string Page { get; set; } = string.Empty;
    /// <summary>Already planned geometry and requested languages; no implicit snapping or translation.</summary>
    public PlacementText Text { get; set; } = new();
}
