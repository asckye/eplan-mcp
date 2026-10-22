using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("Where the pack can be used.")]
public sealed record PackCompatibility
{
    [Description("EPLAN versions as comparators that must all hold, such as '>=2026.0 <2027'; a version without an operator matches by prefix ('2026.0' is every 2026.0.x). Absent: not stated.")]
    public string? EplanVersions { get; init; }

    [Description("EPLAN variants, as EPLAN names them (/Variant:), such as 'Electric P8' or 'Pro Panel'. Empty: not stated.")]
    public IReadOnlyList<string> Variants { get; init; } = [];

    [Description("What the pack needs from EplanMcp or EPLAN: capability catalog ids (catalog/, such as A:backup/PROJECT) or tool names.")]
    [Schema(Pattern = PackPatterns.Capability)]
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
