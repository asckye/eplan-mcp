using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("Another pack this one needs.")]
public sealed record PackDependency
{
    [Schema(Pattern = PackPatterns.Id)]
    public required string Id { get; init; }

    [Description("The versions accepted: comparators that must all hold, such as '^1.2.0', '~1.2.0', '>=1.2.0 <2.0.0', '1.2.3' or '*'.")]
    public required string Version { get; init; }
}
