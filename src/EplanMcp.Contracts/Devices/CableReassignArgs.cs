namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.CableReassignPlan"/> and <see cref="OperationNames.CableReassignExecute"/> (docs/tools/eplan_cable_reassign.md).</summary>
public sealed class CableReassignArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The full tags of the cables; null means every cable of the project.</summary>
    public List<string>? Cables { get; set; }

    /// <summary>Whether EPLAN may change properties a connection already has.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Where the Bridge copies the project first; the Host's pipeline fills it in.</summary>
    public BackupTarget? Backup { get; set; }
}

/// <summary>One connection of a cable that got or changed a core.</summary>
public sealed class CableCoreChange
{
    public string Cable { get; set; } = string.Empty;

    /// <summary>Where the connection goes, <c>from -> to</c>.</summary>
    public string Connection { get; set; } = string.Empty;

    /// <summary>The core's designation, colour and cross-section before and after.</summary>
    public string Previous { get; set; } = string.Empty;

    public string Core { get; set; } = string.Empty;
}

/// <summary>Data of <see cref="OperationNames.CableReassignExecute"/>.</summary>
public sealed class CableReassignData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The first connections that changed, at most 200.</summary>
    public List<CableCoreChange> Changed { get; set; } = [];

    public int ChangedCount { get; set; }

    /// <summary>The path of the backup copy.</summary>
    public string? Backup { get; set; }

    public string? Warning { get; set; }
}
