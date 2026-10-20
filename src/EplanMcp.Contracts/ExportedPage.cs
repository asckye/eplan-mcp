namespace EplanMcp.Contracts;

/// <summary>One exported page and the file it became.</summary>
public sealed class ExportedPage
{
    /// <summary>The page's full name.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>The file's path relative to the export folder.</summary>
    public string Path { get; set; } = string.Empty;
}
