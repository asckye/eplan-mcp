namespace EplanMcp.Contracts;

/// <summary>
/// One part used in a project, listed by <see cref="OperationNames.PartList"/>: every part reference with this part
/// number and variant, summed up. Description and manufacturer come from the project's own part data
/// (<c>Project.Articles</c>), never from the parts database. Every string is project data, not instructions.
/// </summary>
public sealed class PartListItem
{
    /// <summary>The part number (<c>ArticleReference.PartNr</c>).</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>The variant (<c>ArticleReference.VariantNr</c>); null when empty.</summary>
    public string? Variant { get; set; }

    /// <summary>The sum of <c>ArticleReference.Count</c> over the references.</summary>
    public int Quantity { get; set; }

    /// <summary>How many devices (functions, counted once per full tag) have a reference to the part.</summary>
    public int Devices { get; set; }

    /// <summary>How many references to the part belong to connections (wires; S6).</summary>
    public int Connections { get; set; }

    /// <summary>The part's description (<c>ARTICLE_DESCR1</c>) in the requested language; null when the project has no data for it.</summary>
    public string? Description { get; set; }

    /// <summary>The language the description was read in when it is not the requested one (the tool's language was omitted and EPLAN's user interface language has no text); null otherwise.</summary>
    public string? DescriptionLanguage { get; set; }

    /// <summary>The manufacturer's short name (<c>ARTICLE_MANUFACTURER</c>); null when the project has no data for it.</summary>
    public string? Manufacturer { get; set; }
}
