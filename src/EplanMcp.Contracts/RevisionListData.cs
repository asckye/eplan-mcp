namespace EplanMcp.Contracts;

/// <summary>Result data of <see cref="OperationNames.RevisionList"/>: a project's revision state and a page of its revision log.</summary>
public sealed class RevisionListData
{
    /// <summary>The name of the project actually used, so the tool can echo it (mcp-tool-design 7.1).</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The project's link file with its extension (<c>Project.ProjectLinkFilePath</c>).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// <c>none</c> (<c>.elk</c>), <c>tracking</c> (<c>.ell</c>: a revision in progress, changes tracked), <c>completed</c>
    /// (<c>.elr</c>: a completed revision, write-protected) or <c>unknown</c> (another extension), by the link file's
    /// extension (S23 r3, api-notes A28).
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary><c>PROJ_REVISION_LOG_START</c>: whether change tracking has started.</summary>
    public bool TrackingStarted { get; set; }

    /// <summary>For a completed revision, the path of the project it came from (<c>PROJ_REVISIONSPARENT</c>); null otherwise.</summary>
    public string? Parent { get; set; }

    /// <summary>The requested slice of the revision log, by index ascending.</summary>
    public List<RevisionItem> Revisions { get; set; } = [];

    /// <summary>How many records the revision log has.</summary>
    public int Total { get; set; }

    /// <summary>The first pages EPLAN lists as changed and not completed (<c>Revision.GetUncompletedPages</c>), at most 50.</summary>
    public List<string> UncompletedPages { get; set; } = [];

    /// <summary>How many pages EPLAN lists as changed and not completed.</summary>
    public int UncompletedPageCount { get; set; }

    /// <summary>How many pages have an object with a revision marker (<c>PAGE_INSTANCE_WITH_REVISIONMARKER</c>).</summary>
    public int MarkedPageCount { get; set; }
}
