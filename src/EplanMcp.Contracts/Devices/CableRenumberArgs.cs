namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.CableRenumberPlan"/> and <see cref="OperationNames.CableRenumberExecute"/>.</summary>
public sealed class CableRenumberArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>Only these cables, by full or shown tag, 1 to 200; null for every cable of the project.</summary>
    public List<string>? Cables { get; set; }

    /// <summary>The cable numbering scheme's name, required: an empty one would be EPLAN's last used (api-notes A24).</summary>
    public string? Scheme { get; set; }

    /// <summary>True: cables keep their tags and only tags with <c>?</c> are numbered (EPLAN's "keep old names").</summary>
    public bool KeepExisting { get; set; }

    /// <summary>The first number, at least 1.</summary>
    public int Start { get; set; } = 1;

    /// <summary>The step between numbers, at least 1.</summary>
    public int Step { get; set; } = 1;

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}
