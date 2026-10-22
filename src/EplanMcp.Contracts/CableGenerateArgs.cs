namespace EplanMcp.Contracts;

/// <summary>Arguments of cable.generate.plan and cable.generate.execute; fixed safety parameters are not inputs.</summary>
public sealed class CableGenerateArgs
{
    /// <summary>Project name or path from eplan_project_list; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The required project cable creation scheme.</summary>
    public string? CreationScheme { get; set; }

    /// <summary>The required project cable numbering scheme.</summary>
    public string? NumberingScheme { get; set; }

    /// <summary>The required project automatic cable selection scheme.</summary>
    public string? SelectionScheme { get; set; }

    /// <summary>For execute: the fingerprint returned by the plan.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>For execute: the Host's new backup target (ADR-0013).</summary>
    public BackupTarget? Backup { get; set; }
}
