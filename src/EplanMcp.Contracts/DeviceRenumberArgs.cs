namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.DeviceRenumberPlan"/> and of <c>device.renumber.execute</c>.</summary>
public sealed class DeviceRenumberArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>Only the devices on these pages, by their exact full names, 1 to 50; null for the whole project.</summary>
    public List<string>? Pages { get; set; }

    /// <summary>True: only tags with <c>?</c> (EPLAN's <c>PostNumerate</c>); false: every device in scope.</summary>
    public bool OnlyUnnumbered { get; set; } = true;

    /// <summary>The numbering scheme's name; null for EPLAN's default.</summary>
    public string? Scheme { get; set; }

    /// <summary>The first number of each identifier letter, at least 1.</summary>
    public int Start { get; set; } = 1;

    /// <summary>The step between numbers, at least 1.</summary>
    public int Step { get; set; } = 1;

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}
