namespace EplanMcp.Contracts;

/// <summary>Detached report-block settings (A84); unreadable fields are null.</summary>
public sealed class PageReport
{
    /// <summary>DocumentType enumeration name.</summary>
    public string? Type { get; set; }
    /// <summary>Form name without extension.</summary>
    public string? Form { get; set; }
    /// <summary>Report filter scheme name.</summary>
    public string? FilterScheme { get; set; }
    /// <summary>Raw report filter scheme data.</summary>
    public string? FilterSchemeData { get; set; }
    /// <summary>Report sort scheme name.</summary>
    public string? SortScheme { get; set; }
    /// <summary>Raw report sort scheme data.</summary>
    public string? SortSchemeData { get; set; }
    /// <summary>Function filter scheme name.</summary>
    public string? FunctionFilterScheme { get; set; }
    /// <summary>Raw function filter scheme data.</summary>
    public string? FunctionFilterSchemeData { get; set; }
    /// <summary>Function sort scheme name.</summary>
    public string? FunctionSortScheme { get; set; }
    /// <summary>Raw function sort scheme data.</summary>
    public string? FunctionSortSchemeData { get; set; }
    /// <summary>Read-only device tag text; not a supported creation restriction.</summary>
    public string? DeviceTag { get; set; }
    /// <summary>Whether EPLAN generates the page description automatically.</summary>
    public bool? AutomaticPageDescription { get; set; }
    /// <summary>Stored multilingual manual description, including language codes.</summary>
    public string? ManualPageDescription { get; set; }
}
