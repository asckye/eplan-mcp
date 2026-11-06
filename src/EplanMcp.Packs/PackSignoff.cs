using System.ComponentModel;

namespace EplanMcp.Packs;

public sealed record PackSignoff
{
    [Description("draft or signed; signed requires signedBy and signedAt.")]
    [Schema(Pattern = "^(draft|signed)$")]
    public required string Status { get; init; }

    public string? SignedBy { get; init; }

    public string? SignedAt { get; init; }
}
