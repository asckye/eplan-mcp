namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A page-origin free text with session identity and every stored language, including empty entries.</summary>
public sealed class PageFreeText(long oid, string identifier, IReadOnlyList<TextEntry> entries)
{
    public long Oid { get; } = oid;

    public string Identifier { get; } = identifier;

    public IReadOnlyList<TextEntry> Entries { get; } = entries;
}
