namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.TerminalRenumberPlan"/> and <see cref="OperationNames.TerminalRenumberExecute"/>.</summary>
public sealed class TerminalRenumberArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>Only the terminals of these strips, by their exact names, 1 to 50; null for every terminal of the project.</summary>
    public List<string>? Strips { get; set; }

    /// <summary>The terminal numbering scheme's name, required: an empty one would be EPLAN's last used (api-notes A24).</summary>
    public string? Scheme { get; set; }

    /// <summary>True: only terminals whose designation (after <c>:</c>) contains <c>?</c>; false: every terminal in scope.</summary>
    public bool OnlyUnnumbered { get; set; } = true;

    /// <summary>The first number, at least 1.</summary>
    public int Start { get; set; } = 1;

    /// <summary>The step between numbers, at least 1.</summary>
    public int Step { get; set; } = 1;

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}
