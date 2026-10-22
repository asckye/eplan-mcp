namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A project's revision state as <see cref="IRevisionReader"/> reads it (S23 r3, api-notes A28). Project data.</summary>
public sealed class RevisionContent
{
    /// <summary><c>Project.ProjectLinkFilePath</c>, with its extension: <c>.elk</c>, <c>.ell</c> or <c>.elr</c>.</summary>
    public string LinkFile { get; set; } = string.Empty;

    /// <summary><c>PROJ_REVISION_LOG_START</c>; false when not set.</summary>
    public bool TrackingStarted { get; set; }

    /// <summary><c>PROJ_REVISIONSPARENT</c>; null when not set.</summary>
    public string? Parent { get; set; }

    /// <summary>The revision log from index 1 up to the first record without a name.</summary>
    public List<RevisionRecord> Log { get; set; } = [];

    /// <summary>The names of the pages <c>Revision.GetUncompletedPages</c> returns, all of them.</summary>
    public List<string> UncompletedPages { get; set; } = [];

    /// <summary>How many pages have <c>PAGE_INSTANCE_WITH_REVISIONMARKER</c> set.</summary>
    public int MarkedPages { get; set; }
}
