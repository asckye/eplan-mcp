namespace EplanMcp.Contracts;

/// <summary>Report creation settings; scheme names only, with no device restriction (A84).</summary>
public sealed class ReportCreateArgs
{
    /// <summary>Supported DocumentType names from the EPLAN 2026 XML.</summary>
    public static readonly string[] Types = ["TitlePage", "TableOfContents", "PLCDiagram", "TerminalDiagram", "PartsSumList", "PartsList", "CableOverview", "CableConnectionDiagram", "TerminalStripOverview", "DeviceList", "ConnectionList", "StructIdentifierOverview"];
    /// <summary>Explicit project name or path.</summary>
    public string? Project { get; set; }
    /// <summary>DocumentType name.</summary>
    public string? Type { get; set; }
    /// <summary>Form name without extension.</summary>
    public string? Form { get; set; }
    /// <summary>Full first page name.</summary>
    public string? FirstPage { get; set; }
    /// <summary>Optional filter scheme name.</summary>
    public string? FilterScheme { get; set; }
    /// <summary>Optional sorting scheme name.</summary>
    public string? SortScheme { get; set; }
    /// <summary>Null selects automatic description; otherwise manual text.</summary>
    public string? PageDescription { get; set; }
    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
    /// <summary>Shared boundary validation; full page syntax is checked by the Bridge.</summary>
    public List<string> Validate() => new (bool Invalid, string Message)[]
    {
        (string.IsNullOrWhiteSpace(Project), "'project' is required for writes."),
        (!Types.Contains(Type, StringComparer.Ordinal), "'type' must be one of " + string.Join(", ", Types) + "."),
        (string.IsNullOrWhiteSpace(Form) || Form!.Length > 500 || Form.IndexOfAny(['/', '\\', ':', '.']) >= 0, "'form' must be a non-empty form name without path or extension, at most 500 characters."),
        (FilterScheme is not null && (string.IsNullOrWhiteSpace(FilterScheme) || FilterScheme.Length > 500), "'filter_scheme' must be non-empty and at most 500 characters when provided."),
        (SortScheme is not null && (string.IsNullOrWhiteSpace(SortScheme) || SortScheme.Length > 500), "'sort_scheme' must be non-empty and at most 500 characters when provided."),
        (PageDescription?.Length > 500, "'page_description' must be at most 500 characters."),
    }.Where(problem => problem.Invalid).Select(problem => problem.Message).ToList();
}
