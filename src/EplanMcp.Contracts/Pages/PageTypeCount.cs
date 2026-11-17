namespace EplanMcp.Contracts;

/// <summary>How many pages of a project have one page type.</summary>
public sealed class PageTypeCount
{
    /// <summary>EPLAN's page type name (<c>DocumentTypeManager.DocumentType</c>), for example <c>Circuit</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Number of pages of this type.</summary>
    public int Count { get; set; }
}
