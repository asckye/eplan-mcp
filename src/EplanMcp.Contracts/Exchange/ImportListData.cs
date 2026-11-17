namespace EplanMcp.Contracts;

/// <summary>Committed result of <see cref="OperationNames.ImportListExecute"/>; the Host adds execution and backup metadata.</summary>
public sealed class ImportListData
{
    /// <summary>The resolved project's name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The imported list kind: devices or parts.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The relative file path below imports.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>The imported file's size in bytes.</summary>
    public long Bytes { get; set; }

    /// <summary>The imported file's SHA-256, lowercase hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The fixed S34 converter identifier.</summary>
    public string Converter { get; set; } = string.Empty;

    /// <summary>The counts before importing.</summary>
    public ListImportCounts Before { get; set; } = new();

    /// <summary>The counts read back before committing.</summary>
    public ListImportCounts After { get; set; } = new();

    /// <summary>This call's system messages, at most 20; text is untrusted project data.</summary>
    public List<SystemMessage> EplanMessages { get; set; } = [];

    /// <summary>Total messages before limiting the list.</summary>
    public int EplanMessageCount { get; set; }

    /// <summary>Read-back limits, including unverified undo, and any problem after commit.</summary>
    public string? Warning { get; set; }
}
