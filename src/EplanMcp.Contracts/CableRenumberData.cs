namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.CableRenumberExecute"/>: the cables EPLAN renamed, as read back. Tags are project data.</summary>
public sealed class CableRenumberData
{
    /// <summary>The name of the project the cables are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Each cable whose full tag changed (<c>+A2-WD4</c> → <c>+A2-WD5</c>), once, in EPLAN's order, at most 200.</summary>
    public List<RenamedObject> Changed { get; set; } = [];

    /// <summary>How many cables changed their tag, before the list was cut.</summary>
    public int ChangedCount { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
