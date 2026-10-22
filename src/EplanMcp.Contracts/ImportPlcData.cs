namespace EplanMcp.Contracts;

/// <summary>Result of <see cref="OperationNames.ImportPlcExecute"/> after the mandatory transaction checks passed.</summary>
public sealed class ImportPlcData
{
    /// <summary>The target project's name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The imported file's relative path below the import folder.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>The imported file's size in bytes.</summary>
    public long Bytes { get; set; }

    /// <summary>The imported file's SHA-256, lowercase hex.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The converter used for the import.</summary>
    public string Converter { get; set; } = string.Empty;

    /// <summary>Counts before the action.</summary>
    public ImportPlcCounts Before { get; set; } = new();

    /// <summary>Counts after the action, before commit.</summary>
    public ImportPlcCounts After { get; set; } = new();

    /// <summary>The increase in the function count.</summary>
    public int AddedFunctions { get; set; }

    /// <summary>The increase in the PLC box count.</summary>
    public int AddedPlcBoxes { get; set; }

    /// <summary>New objects whose full name is shared with another new object, counted by identity; informational only.</summary>
    public int NewObjectsSharingNames { get; set; }

    /// <summary>Only this call's messages, as copied records, at most 20 (api-notes A11).</summary>
    public List<SystemMessage> EplanMessages { get; set; } = [];

    /// <summary>Total number of this call's non-trace messages, before truncation.</summary>
    public int EplanMessageCount { get; set; }

    /// <summary>A problem after commit; null when none. Undo after commit remains unverified (A39).</summary>
    public string? Warning { get; set; }
}
