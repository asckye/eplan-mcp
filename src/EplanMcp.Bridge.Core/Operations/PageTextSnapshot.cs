namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Complete read-only free-text inventory of an exact page, including an empty page.</summary>
public sealed class PageTextSnapshot(string name, string identifier, IReadOnlyList<PageFreeText> texts)
{
    public string Name { get; } = name;

    public string Identifier { get; } = identifier;

    public IReadOnlyList<PageFreeText> Texts { get; } = texts;
}
