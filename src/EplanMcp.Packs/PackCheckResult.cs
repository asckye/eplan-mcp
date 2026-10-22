namespace EplanMcp.Packs;

/// <summary>What a check of a pack found: the manifest and lock when they could be read, and every problem.</summary>
public sealed record PackCheckResult(PackManifest? Manifest, PackLock? Lock, IReadOnlyList<PackProblem> Problems)
{
    public bool Ok => Problems.Count == 0;

    internal static PackCheckResult Failed(string location, string message) => new(null, null, [new PackProblem(location, message)]);
}
