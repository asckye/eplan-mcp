using System.ComponentModel;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs;

[Description("One cabinet of the project.")]
public sealed record CabinetBinding
{
    [Schema(Pattern = PackPatterns.Id)]
    public required string Id { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<string>? Layers { get; init; }

    public JsonObject? Values { get; init; }

    public JsonObject? Structure { get; init; }
}
