namespace EplanMcp.Packs;

/// <summary>
/// What a change of a development pack did: the new manifest and its pack digest, whether the pack was created, the
/// resources added and replaced, the paths left as they were, and the gap documents written.
/// </summary>
public sealed record DevPackChange(
    PackManifest Manifest,
    string Digest,
    bool Created,
    IReadOnlyList<PackResource> Added,
    IReadOnlyList<PackResource> Replaced,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<string> Gaps);
