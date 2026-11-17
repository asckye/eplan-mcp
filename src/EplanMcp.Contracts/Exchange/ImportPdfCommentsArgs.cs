namespace EplanMcp.Contracts;

/// <summary>Arguments of import.pdf_comments.plan/execute; the Host validates file intake (SEC-31).</summary>
public sealed class ImportPdfCommentsArgs
{
    /// <summary>Explicit project name or path.</summary>
    public string? Project { get; set; }
    /// <summary>Relative PDF name below Host imports.</summary>
    public string? File { get; set; }
    /// <summary>Host-resolved full path; never exposed in errors.</summary>
    public string? FilePath { get; set; }
    /// <summary>Host-computed lowercase SHA-256.</summary>
    public string? Sha256 { get; set; }
    /// <summary>Plan fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}
