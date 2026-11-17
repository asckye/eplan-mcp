namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectOpenPlan"/> and <see cref="OperationNames.ProjectOpenExecute"/> (docs/tools/eplan_project_open.md).</summary>
public sealed class ProjectOpenArgs
{
    /// <summary>The full path of the project's <c>.elk</c> file.</summary>
    public string? Path { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>Arguments of <see cref="OperationNames.ProjectClosePlan"/> and <see cref="OperationNames.ProjectCloseExecute"/> (docs/tools/eplan_project_close.md).</summary>
public sealed class ProjectCloseArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required.</summary>
    public string? Project { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>Data of <see cref="OperationNames.ProjectOpenExecute"/> and <see cref="OperationNames.ProjectCloseExecute"/>.</summary>
public sealed class ProjectSessionData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The project's path as eplan_project_list returns it.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether it is EPLAN's selected project after the call.</summary>
    public bool Current { get; set; }
}

/// <summary>Arguments of <see cref="OperationNames.ProjectRenamePlan"/> and <see cref="OperationNames.ProjectRenameExecute"/> (docs/tools/eplan_project_rename.md).</summary>
public sealed class ProjectRenameArgs
{
    /// <summary>Open project, name or path as eplan_project_list returns it; required.</summary>
    public string? Project { get; set; }

    /// <summary>The new project name, without extension or path; required.</summary>
    public string? NewName { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Where the Bridge copies the project first; the Host's pipeline fills it in.</summary>
    public BackupTarget? Backup { get; set; }
}

/// <summary>Data of <see cref="OperationNames.ProjectRenameExecute"/>.</summary>
public sealed class ProjectRenameData
{
    /// <summary>The project under its new name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Its path as eplan_project_list returns it.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The old project's path; its files are removed.</summary>
    public string PreviousPath { get; set; } = string.Empty;

    /// <summary>Whether it is EPLAN's selected project after the call.</summary>
    public bool Current { get; set; }

    /// <summary>The path of the backup copy.</summary>
    public string? Backup { get; set; }

    public string? Warning { get; set; }
}
