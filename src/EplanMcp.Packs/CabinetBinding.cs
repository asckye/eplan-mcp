using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("One cabinet of the project.")]
public sealed record CabinetBinding
{
    [Schema(Pattern = PackPatterns.Id)]
    public required string Id { get; init; }

    public string? Title { get; init; }
}
