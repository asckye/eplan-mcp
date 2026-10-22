namespace EplanMcp.Packs;

/// <summary>What an install did: the outcome, the pack, and every problem when it was refused.</summary>
public sealed record PackInstallResult(PackInstallOutcome Outcome, PackManifest? Manifest, IReadOnlyList<PackProblem> Problems);
