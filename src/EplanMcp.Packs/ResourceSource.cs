using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("Where the resource came from: an input of provenance.inputs and the path below it. Never a local absolute path, so a pack can be shared.")]
public sealed record ResourceSource
{
    [Description("The input's name, as provenance.inputs lists it.")]
    public required string Input { get; init; }

    [Description("The path below that input, '/' between folders; empty when the input is the file itself.")]
    public required string Path { get; init; }
}
