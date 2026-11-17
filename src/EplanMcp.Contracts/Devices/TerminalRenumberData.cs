namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.TerminalRenumberExecute"/>: the terminals EPLAN renamed, as read back. Names are project data.</summary>
public sealed class TerminalRenumberData
{
    /// <summary>The name of the project the terminals are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Each terminal whose full name changed (<c>+A1-XD1:2</c> → <c>+A1-XD1:3</c>), once, in EPLAN's order, at most 200.</summary>
    public List<RenamedObject> Changed { get; set; } = [];

    /// <summary>How many terminals changed their name, before the list was cut.</summary>
    public int ChangedCount { get; set; }

    /// <summary>Terminals in scope whose designation still contains <c>?</c> after numbering, at most 50.</summary>
    public List<string> StillUnnumbered { get; set; } = [];

    /// <summary>How many such terminals there are, before the list was cut.</summary>
    public int StillUnnumberedCount { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
