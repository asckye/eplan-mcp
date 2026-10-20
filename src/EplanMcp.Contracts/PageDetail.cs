namespace EplanMcp.Contracts;

/// <summary>One page as <see cref="OperationNames.PageGet"/> describes it.</summary>
public sealed class PageDetail
{
    /// <summary>The full page name (<c>Page.Name</c>), for example <c>=GAA+A1&amp;EFS1/1</c> (api-notes A11).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>EPLAN's page type name, for example <c>Circuit</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The page type as EPLAN names it in its user interface language (<c>Page.PageTypeName</c>); null when empty.</summary>
    public string? TypeName { get; set; }

    /// <summary>The page description in the requested language; null when the page has none.</summary>
    public string? Description { get; set; }

    /// <summary>The language the description was read in when it is not the requested one (the tool's language was omitted and EPLAN's user interface language has no text); null otherwise.</summary>
    public string? DescriptionLanguage { get; set; }
}
