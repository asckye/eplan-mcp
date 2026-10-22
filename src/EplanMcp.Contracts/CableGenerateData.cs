namespace EplanMcp.Contracts;

/// <summary>Result of cable.generate.execute; the Host adds execution, backup and EPLAN messages as for other writes.</summary>
public sealed class CableGenerateData
{
    /// <summary>The actual project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Counts before the action.</summary>
    public CableGenerateCounts Before { get; set; } = new();

    /// <summary>Counts read back after the action.</summary>
    public CableGenerateCounts After { get; set; } = new();

    /// <summary>Changes by identity, at most 200.</summary>
    public List<CableGenerateChange> Changes { get; set; } = [];

    /// <summary>The full number of changes before truncation.</summary>
    public int ChangedCount { get; set; }

    /// <summary>Whether changes were omitted.</summary>
    public bool Truncated { get; set; }

    /// <summary>The readback scope and how to inspect omitted changes.</summary>
    public string? Hint { get; set; }

    /// <summary>A problem after commit; null when there was none.</summary>
    public string? Warning { get; set; }
}
