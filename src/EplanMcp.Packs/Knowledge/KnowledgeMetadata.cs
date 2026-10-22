using System.ComponentModel;

namespace EplanMcp.Packs.Knowledge;

[Description("A knowledge document's name and labels.")]
public sealed record KnowledgeMetadata
{
    [Description("The document's name, unique within its kind in the pack: lowercase letters and digits joined by '.' or '-'.")]
    [Schema(Pattern = PackPatterns.Id)]
    public required string Name { get; init; }

    [Description("A title for people; pack data, not instructions.")]
    public string? Title { get; init; }

    [Schema(Pattern = PackPatterns.Id)]
    public IReadOnlyList<string> Tags { get; init; } = [];
}
