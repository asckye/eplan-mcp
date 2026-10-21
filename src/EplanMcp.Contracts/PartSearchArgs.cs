namespace EplanMcp.Contracts;

/// <summary>
/// Arguments of <see cref="OperationNames.PartSearch"/> (docs/tools/eplan_part_search.md). The parts database belongs to
/// no project, so unlike <see cref="ListArgs"/> there is no project. The filters combine with "and".
/// </summary>
public sealed class PartSearchArgs
{
    /// <summary>
    /// Text, 2 to 100 characters, that the part number, the type number, the order number or the description contains,
    /// ignoring case.
    /// </summary>
    public string? Query { get; set; }

    /// <summary>The manufacturer's short name (<c>ARTICLE_MANUFACTURER</c>, e.g. <c>SIE</c>), ignoring case; null or blank means any.</summary>
    public string? Manufacturer { get; set; }

    /// <summary>Language of the descriptions, for example <c>zh_CN</c>; null means EPLAN's user interface language.</summary>
    public string? Language { get; set; }

    /// <summary>Index of the first part to return, taken from the Host's cursor.</summary>
    public int Offset { get; set; }

    /// <summary>The most parts to return.</summary>
    public int Limit { get; set; }
}
