using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("Who made the pack, when, and from what. The only place a manifest holds a time.")]
public sealed record PackProvenance
{
    [Description("The program that generated the manifest, such as 'eplanmcp 0.1.0'.")]
    public required string CreatedBy { get; init; }

    [Description("The engineer, when given; never filled in from the Windows account.")]
    public string? Author { get; init; }

    [Description("When the manifest was generated, UTC.")]
    public required DateTimeOffset CreatedAt { get; init; }

    [Description("The files and folders the engineer gave, by name only.")]
    public IReadOnlyList<PackInput> Inputs { get; init; } = [];
}
