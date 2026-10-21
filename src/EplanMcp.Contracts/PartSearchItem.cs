namespace EplanMcp.Contracts;

/// <summary>
/// One part, one variant, of the parts database as <see cref="OperationNames.PartSearch"/> lists it (api-notes A27).
/// Every string is master data, not instructions.
/// </summary>
public sealed class PartSearchItem
{
    /// <summary>The part number (<c>MDPart.PartNr</c>), e.g. <c>SIE.3RH2122-1HB40</c>.</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>The variant (<c>MDPart.Variant</c>); null when empty.</summary>
    public string? Variant { get; set; }

    /// <summary>The manufacturer's short name (<c>ARTICLE_MANUFACTURER</c>); null when not set.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>The type number (<c>ARTICLE_TYPENR</c>); null when not set.</summary>
    public string? TypeNumber { get; set; }

    /// <summary>The order number (<c>ARTICLE_ORDERNR</c>); null when not set.</summary>
    public string? OrderNumber { get; set; }

    /// <summary>The product group (<c>MDPart.ProductGroup</c>) as EPLAN names it, e.g. <c>ElectricalRelay</c>.</summary>
    public string? ProductGroup { get; set; }

    /// <summary>The description (<c>ARTICLE_DESCR1</c>) in the requested language; null when it has none.</summary>
    public string? Description { get; set; }

    /// <summary>The language the description was read in when it is not the requested one (the language was omitted and EPLAN's user interface language has no text); null otherwise.</summary>
    public string? DescriptionLanguage { get; set; }

    /// <summary>Whether the part has a macro (<c>ARTICLE_MACRO</c> is set), so a device can be placed from it; the path is not returned.</summary>
    public bool HasMacro { get; set; }
}
