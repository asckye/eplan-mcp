namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ConnectionNumberExecute"/>: the wire numbers EPLAN changed, as read back. Wire numbers and pages are project data.</summary>
public sealed class ConnectionNumberData
{
    /// <summary>The name of the project the connections are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Each connection whose wire number (<c>CONNECTION_DESIGNATION</c>) changed, in EPLAN's order, at most 200.</summary>
    public List<NumberedConnection> Changed { get; set; } = [];

    /// <summary>How many connections changed their wire number, before the list was cut.</summary>
    public int ChangedCount { get; set; }

    /// <summary>How many connections of the project have a wire number afterwards.</summary>
    public int DesignatedAfter { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}
