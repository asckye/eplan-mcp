using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("One exact pack version, by the SHA-256 of its pack.json; the manifest lists every resource's hash, so this pins the content too.")]
public sealed record PackReference
{
    [Schema(Pattern = PackPatterns.Id)]
    public required string Id { get; init; }

    [Schema(Pattern = PackPatterns.SemVer)]
    public required string Version { get; init; }

    [Schema(Pattern = PackPatterns.Sha256)]
    public required string ManifestSha256 { get; init; }
}
