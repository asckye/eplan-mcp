using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("A value EplanMcp read from a resource's files.")]
public sealed record PackFact
{
    [Description("The resource's path in this pack.")]
    public required string Resource { get; init; }

    [Description("What was read, such as eplan_version or archive_format.")]
    [Schema(Pattern = PackPatterns.Name)]
    public required string Name { get; init; }

    public required string Value { get; init; }

    [Description("Where it was read, such as 'ProjectInfo.xml property 10088'.")]
    public required string Evidence { get; init; }
}
