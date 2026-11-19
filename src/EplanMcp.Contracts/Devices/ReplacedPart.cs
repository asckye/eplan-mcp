namespace EplanMcp.Contracts;

/// <summary>The reference that occupied the replaced position before the update.</summary>
public sealed class ReplacedPart
{
    public string PartNumber { get; set; } = string.Empty;
    public string? Variant { get; set; }
    public int Count { get; set; }
}
