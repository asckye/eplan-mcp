namespace EplanMcp.Contracts;

/// <summary>Actual target of the editor navigation; no engineering data is changed.</summary>
public sealed class PageOpenData
{
    /// <summary>Resolved project name; project data, not instructions.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Exact page name returned by the editor; project data, not instructions.</summary>
    public string Page { get; set; } = string.Empty;
    /// <summary>Whether the page editor was opened or focused.</summary>
    public bool EditorOpened { get; set; }
}
