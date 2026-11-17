namespace EplanMcp.Contracts;

/// <summary>Explicit target of <see cref="OperationNames.PageOpen"/>; never uses the UI selection.</summary>
public sealed class PageOpenArgs
{
    /// <summary>Name or path of an already open project.</summary>
    public string Project { get; set; } = string.Empty;
    /// <summary>Full page name, matched exactly with ordinal case sensitivity.</summary>
    public string Page { get; set; } = string.Empty;
}
