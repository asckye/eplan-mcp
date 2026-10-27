namespace EplanMcp.Contracts;

/// <summary>A read-only scratch copy; its opaque handle belongs to the Bridge that prepared it.</summary>
public sealed class ReferenceCopyData
{
    /// <summary>The single-use close handle; project GUIDs do not distinguish copies (A34).</summary>
    public string Handle { get; set; } = string.Empty;
    /// <summary>The copy's .elk path.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>The database version checked before opening (A31).</summary>
    public string DatabaseVersion { get; set; } = string.Empty;
    /// <summary>The current project's path before preparing; null when none is current.</summary>
    public string? CurrentBefore { get; set; }
    /// <summary>The current project's path afterwards; opening need not select the copy (A46).</summary>
    public string? CurrentAfter { get; set; }
}
