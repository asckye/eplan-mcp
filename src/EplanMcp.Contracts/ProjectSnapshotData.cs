namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ProjectSnapshot"/>: one chunk of the snapshot.</summary>
public sealed class ProjectSnapshotData
{
    /// <summary>The project's name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The project's path without extension, as EPLAN reports it: the project of every locator.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>The project's <c>PROJ_GUID</c>, <c>name@unix-seconds</c>: shared by file copies and restored backups (A34).</summary>
    public string? ProjectGuid { get; set; }

    /// <summary>The project's last modification date and time as EPLAN stores them, a hint only (A36).</summary>
    public string? Modified { get; set; }

    /// <summary>The profile used.</summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary><see cref="SnapshotProfiles.Format"/>.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>How many pages the call covers over all its chunks: the project's, or the found ones of <see cref="ProjectSnapshotArgs.Pages"/>.</summary>
    public int PageCount { get; set; }

    /// <summary>The pages of this chunk.</summary>
    public List<SnapshotPage> Pages { get; set; } = [];

    /// <summary>Names in <see cref="ProjectSnapshotArgs.Pages"/> the project has no page for.</summary>
    public List<string> MissingPages { get; set; } = [];

    /// <summary>Whether this chunk ends the call's pages.</summary>
    public bool Complete { get; set; }

    /// <summary>When not complete: the <see cref="ProjectSnapshotArgs.AfterPage"/> of the next call.</summary>
    public long? NextAfterPage { get; set; }

    /// <summary>The snapshot hash (design 4.4), only when this one chunk covers every page of the project.</summary>
    public string? SnapshotHash { get; set; }

    /// <summary>Connections of the project a snapshot leaves out: 3D connections and connections on no page.</summary>
    public int SkippedConnections { get; set; }

    /// <summary>How long the chunk took, in milliseconds.</summary>
    public long Milliseconds { get; set; }
}
