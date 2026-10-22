namespace EplanMcp.Contracts;

/// <summary>Result of cable.generate.execute; the Host adds execution and backup metadata.</summary>
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

    /// <summary>This call's system messages, at most 20; text is untrusted project data.</summary>
    public List<SystemMessage> EplanMessages { get; set; } = [];

    /// <summary>Total messages before limiting the list.</summary>
    public int EplanMessageCount { get; set; }

    /// <summary>The readback scope and how to inspect omitted changes.</summary>
    public string? Hint { get; set; }

    /// <summary>A message collection or post-commit problem; null when there was none.</summary>
    public string? Warning { get; set; }
}
