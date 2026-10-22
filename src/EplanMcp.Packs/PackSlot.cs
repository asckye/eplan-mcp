using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("A resource given a role in the pack.")]
public sealed record PackSlot
{
    [Description("The resource's path in this pack.")]
    public required string Path { get; init; }

    public required KnowledgeStanding Standing { get; init; }

    [Description("What it is for, in a few words.")]
    public string? Note { get; init; }
}
