namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.PartList"/> (docs/tools/eplan_part_list.md). The filters combine with "and";
/// null or blank means no filter.
/// </summary>
public sealed class PartListArgs : ListArgs
{
    /// <summary>Text the manufacturer (<c>ARTICLE_MANUFACTURER</c>, e.g. <c>SIE</c>) contains, ignoring case.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Text the part number or the description contains, ignoring case.</summary>
    public string? Text { get; set; }

    /// <summary>Language of the descriptions, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }
}
