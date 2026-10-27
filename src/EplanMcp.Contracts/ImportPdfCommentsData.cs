namespace EplanMcp.Contracts;

/// <summary>Read-back of a PDF sticky-note import (A39/S44).</summary>
public sealed class ImportPdfCommentsData : IWriteExecution
{
    /// <summary>Actual project name.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Committed, or not_started when no comments were added.</summary>
    public string Execution => Added == 0 ? ExecutionState.NotStarted : ExecutionState.Committed;
    /// <summary>Comment count before import.</summary>
    public int Before { get; set; }
    /// <summary>Comment count after import.</summary>
    public int After { get; set; }
    /// <summary>Total new comments, independent of the bounded list.</summary>
    public int Added { get; set; }
    /// <summary>At most 200 new comments; untrusted project data.</summary>
    public List<PdfComment> Comments { get; set; } = [];
    /// <summary>Total new EPLAN system messages.</summary>
    public int EplanMessageCount { get; set; }
    /// <summary>At most 20 new system messages; untrusted data.</summary>
    public List<SystemMessage> EplanMessages { get; set; } = [];
    /// <summary>Post-commit warning, if any.</summary>
    public string? Warning { get; set; }
}
