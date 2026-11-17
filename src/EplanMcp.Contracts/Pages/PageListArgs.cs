namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.PageList"/>. The filters combine with AND; null means no filter.</summary>
public sealed class PageListArgs : ListArgs
{
    /// <summary>Start of the full page name, case-sensitive as in EPLAN, for example <c>=GAA+A1</c>.</summary>
    public string? NamePrefix { get; set; }

    /// <summary>EPLAN's page type name (<c>DocumentTypeManager.DocumentType</c>), for example <c>TerminalDiagram</c>; case is ignored.</summary>
    public string? PageType { get; set; }

    /// <summary>Text the page description must contain in <see cref="Language"/>, ignoring case.</summary>
    public string? Text { get; set; }

    /// <summary>Language of the page descriptions, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }
}
