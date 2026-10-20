namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.DeviceRenumberExecute"/>: the tags EPLAN changed, as read back. Tags are project data.</summary>
public sealed class DeviceRenumberData
{
    /// <summary>The name of the project the devices are in.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Each device whose tag changed, once, in EPLAN's order, at most 200.</summary>
    public List<RenamedObject> Changed { get; set; } = [];

    /// <summary>How many devices changed their tag, before the list was cut.</summary>
    public int ChangedCount { get; set; }

    /// <summary>Devices in scope whose tag still contains <c>?</c> after numbering, at most 50.</summary>
    public List<string> StillUnnumbered { get; set; } = [];

    /// <summary>How many such devices there are, before the list was cut.</summary>
    public int StillUnnumberedCount { get; set; }

    /// <summary>A problem after the commit, for example an undo step EPLAN did not close; null when there was none.</summary>
    public string? Warning { get; set; }
}

