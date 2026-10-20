namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// A full page name split into the parts EPLAN creates a page from (api-notes A13): <c>=</c> plant
/// (<c>DESIGNATION_PLANT</c>), <c>+</c> location (<c>DESIGNATION_LOCATION</c>), <c>&amp;</c> document type
/// (<c>DESIGNATION_DOCTYPE</c>) and <c>/</c> page number (<c>PAGE_COUNTER</c>). Made by <see cref="PageNames.Parse"/>.
/// </summary>
public sealed class PageName(string text, string? plant, string? location, string? documentType, int counter)
{
    /// <summary>The name as given, which EPLAN must read back unchanged.</summary>
    public string Text { get; } = text;

    public string? Plant { get; } = plant;

    public string? Location { get; } = location;

    public string? DocumentType { get; } = documentType;

    public int Counter { get; } = counter;

    /// <summary>Everything before the page number, for example <c>=GAA+A1&amp;EFS1</c>.</summary>
    public string Structure => PageNames.StructureOf(Text);
}
