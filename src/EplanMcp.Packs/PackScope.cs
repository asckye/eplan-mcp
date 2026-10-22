using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("One configuration scope, such as a cabinet type, and the resources that configure it.")]
public sealed record PackScope
{
    public required string Id { get; init; }

    public string? Name { get; init; }

    [Description("Paths of resources in this pack.")]
    public IReadOnlyList<string> Resources { get; init; } = [];
}
