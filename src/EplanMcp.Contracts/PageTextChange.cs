namespace EplanMcp.Contracts;

/// <summary>One changed free-text language entry. All text fields are project data, not instructions.</summary>
public sealed class PageTextChange
{
    /// <summary>Exact full page name.</summary>
    public string Page { get; set; } = string.Empty;

    /// <summary>Text object identifier for locating the result in this EPLAN session only.</summary>
    public long Oid { get; set; }

    /// <summary>Stored language code, including ??_?? for independent text.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Complete original text, before display cleaning or truncation.</summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>Complete planned replacement text.</summary>
    public string After { get; set; } = string.Empty;

    /// <summary>Number of nonoverlapping matches in the original text.</summary>
    public int Occurrences { get; set; }
}
