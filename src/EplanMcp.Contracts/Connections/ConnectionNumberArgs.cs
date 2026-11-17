namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ConnectionNumberPlan"/> and <see cref="OperationNames.ConnectionNumberExecute"/>.</summary>
public sealed class ConnectionNumberArgs
{
    /// <summary>
    /// The values of <see cref="Overwrite"/>, the default first: keep wire numbers entered by hand and overwrite the
    /// others; keep every existing one; overwrite all (EPLAN's <c>DesignateOverwrition</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> OverwriteModes = ["except_manual", "none", "all"];

    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The wire numbering scheme's name, required: an empty one would be EPLAN's last used (api-notes A24).</summary>
    public string? Scheme { get; set; }

    /// <summary>Which existing wire numbers EPLAN may replace: one of <see cref="OverwriteModes"/>.</summary>
    public string Overwrite { get; set; } = "except_manual";

    /// <summary>For the execute operation: the plan's <see cref="WritePlan.Fingerprint"/>. Null for the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For the execute operation: where to back the project up first (ADR-0013). Null for the plan.</summary>
    public BackupTarget? Backup { get; set; }
}
