namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectSnapshot"/> (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.1).</summary>
public sealed class ProjectSnapshotArgs
{
    /// <summary>The project as eplan_project_list gives it; omitted means the selected or only open one.</summary>
    public string? Project { get; set; }

    /// <summary>Full names of the pages to take; null takes every page.</summary>
    public List<string>? Pages { get; set; }

    /// <summary>Continues after this page: the <see cref="ProjectSnapshotData.NextAfterPage"/> of the previous chunk.</summary>
    public long? AfterPage { get; set; }

    /// <summary>The profile; null is <see cref="SnapshotProfiles.StandardName"/>, the only one.</summary>
    public string? Profile { get; set; }

    /// <summary>How long one chunk may read, in seconds: 1 to 20, default 10.</summary>
    public int? TimeLimitSeconds { get; set; }

    /// <summary>
    /// With <see cref="Project"/> the full path of the .elk file of a project that is not open: opens it read-only for
    /// this call and closes it again. The Host passes it only for a copy in its scratch folder.
    /// </summary>
    public bool OpenReadOnly { get; set; }
}
