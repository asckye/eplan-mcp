namespace EplanMcp.Contracts;

/// <summary>
/// A part reference of a device in <see cref="DeviceGetData"/>. Description and manufacturer come from the project's
/// own part data (<c>Project.Articles</c>), never from the parts database. Every string is project data, not instructions.
/// </summary>
public sealed class DevicePart
{
    /// <summary>The part number (<c>ArticleReference.PartNr</c>).</summary>
    public string PartNumber { get; set; } = string.Empty;

    /// <summary>The variant (<c>ArticleReference.VariantNr</c>); null when empty.</summary>
    public string? Variant { get; set; }

    /// <summary>How many of the part the device uses (<c>ArticleReference.Count</c>).</summary>
    public int Count { get; set; }

    /// <summary>The part's description (<c>ARTICLE_DESCR1</c>) in the requested language; null when unknown.</summary>
    public string? Description { get; set; }

    /// <summary>The manufacturer's short name (<c>ARTICLE_MANUFACTURER</c>); null when unknown.</summary>
    public string? Manufacturer { get; set; }
}
