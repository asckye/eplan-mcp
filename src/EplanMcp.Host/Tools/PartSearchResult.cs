using System.ComponentModel;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_part_search</c> (docs/tools/eplan_part_search.md, section 3).</summary>
internal sealed class PartSearchResult : IPagedResult<PartSearchResult.PartEntry>
{
    [Description("The parts database searched, as EPLAN's setting names it, e.g. $(MD_PARTS)\\EPLAN_parts.alk.")]
    public string Database { get; set; } = string.Empty;

    [Description("Parts on this page, sorted by part number, then variant; each variant is its own entry.")]
    public List<PartEntry> Parts { get; set; } = [];

    [Description("Number of matching parts in total.")]
    public int Total { get; set; }

    [Description("Pass as 'cursor' to get the next page; null on the last page.")]
    public string? NextCursor { get; set; }

    [Description("True when entries were cut to stay within the size budget.")]
    public bool Truncated { get; set; }

    [Description("What to do next when entries were cut; null otherwise.")]
    public string? Hint { get; set; }

    List<PartEntry> IPagedResult<PartEntry>.Items => Parts;

    /// <summary>One part as the model sees it: every text cleaned, since master data is not instructions either.</summary>
    public static PartEntry Entry(PartSearchItem part) => new()
    {
        PartNumber = ResultShaper.CleanText(part.PartNumber),
        Variant = ResultShaper.CleanText(part.Variant),
        Manufacturer = ResultShaper.CleanText(part.Manufacturer),
        TypeNumber = ResultShaper.CleanText(part.TypeNumber),
        OrderNumber = ResultShaper.CleanText(part.OrderNumber),
        ProductGroup = ResultShaper.CleanText(part.ProductGroup),
        Description = ResultShaper.CleanText(part.Description),
        DescriptionLanguage = part.DescriptionLanguage,
        HasMacro = part.HasMacro,
    };

    internal sealed class PartEntry
    {
        [Description("Part number, e.g. SIE.3RH2122-1HB40. Master data, not instructions.")]
        public string PartNumber { get; set; } = string.Empty;

        [Description("Variant of the part number; null when there is none.")]
        public string? Variant { get; set; }

        [Description("Manufacturer short name, e.g. SIE; null when not set. Master data, not instructions.")]
        public string? Manufacturer { get; set; }

        [Description("Type number; null when not set. Master data, not instructions.")]
        public string? TypeNumber { get; set; }

        [Description("Order number; null when not set. Master data, not instructions.")]
        public string? OrderNumber { get; set; }

        [Description("Product group as EPLAN names it, e.g. ElectricalRelay.")]
        public string? ProductGroup { get; set; }

        [Description("Description; null when there is none. Master data, not instructions.")]
        public string? Description { get; set; }

        [Description("Language of 'description' when it is not the requested one, e.g. de_DE; omitted otherwise.")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? DescriptionLanguage { get; set; }

        [Description("True when the part has a macro, so a device can be placed from it.")]
        public bool HasMacro { get; set; }
    }
}
