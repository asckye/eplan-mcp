using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("pack.lock.json beside pack.json: the pack itself and every pack it needs, directly or through another, at the exact version resolved. Sorted by id and without times, so one resolution gives one file.")]
public sealed record PackLock
{
    public const int CurrentLockVersion = 1;

    [Description("The lock format. A reader refuses a version it does not know.")]
    [Schema(Const = CurrentLockVersion)]
    public required int LockVersion { get; init; }

    public required PackReference Pack { get; init; }

    public IReadOnlyList<PackReference> Dependencies { get; init; } = [];

    public static PackLock Create(PackReference pack, IEnumerable<PackReference> resolved) => new()
    {
        LockVersion = CurrentLockVersion,
        Pack = pack,
        Dependencies = [.. resolved.OrderBy(r => r.Id, StringComparer.Ordinal)],
    };

    public static PackLock Read(byte[] utf8) => PackJson.Read<PackLock>(utf8, PackManifest.LockFileName, "lockVersion", CurrentLockVersion);

    /// <summary>What makes this lock wrong for <paramref name="manifest"/>, whose pack.json has <paramref name="manifestSha256"/>.</summary>
    public IReadOnlyList<PackProblem> Problems(PackManifest manifest, string manifestSha256)
    {
        var problems = new List<PackProblem>();
        if (Pack.Id != manifest.Id || Pack.Version != manifest.Version || Pack.ManifestSha256 != manifestSha256)
        {
            problems.Add(new("pack.lock.json: pack", "is not this pack.json (id, version or SHA-256 differ); lock the pack again"));
        }

        for (int i = 0; i < Dependencies.Count; i++)
        {
            PackReference locked = Dependencies[i];
            if (!PackManifestValidator.IsId(locked.Id) || !SemanticVersion.TryParse(locked.Version, out _) || !PackManifestValidator.IsSha256(locked.ManifestSha256))
            {
                problems.Add(new($"pack.lock.json: dependencies[{i}]", "needs an id, a SemVer version and a SHA-256"));
            }
            else if (i > 0 && string.CompareOrdinal(Dependencies[i - 1].Id, locked.Id) >= 0)
            {
                problems.Add(new($"pack.lock.json: dependencies[{i}]", "must be sorted by id, each id once"));
            }
        }

        foreach (PackDependency dependency in manifest.Dependencies)
        {
            PackReference? locked = Dependencies.FirstOrDefault(d => d.Id == dependency.Id);
            if (locked is null)
            {
                problems.Add(new("pack.lock.json: dependencies", $"has no version of '{dependency.Id}', which pack.json needs"));
            }
            else if (VersionRange.TryParse(dependency.Version, out VersionRange? range) && SemanticVersion.TryParse(locked.Version, out SemanticVersion? version)
                && !range.Contains(version))
            {
                problems.Add(new("pack.lock.json: dependencies", $"locks '{dependency.Id}' at {locked.Version}, outside '{dependency.Version}'"));
            }
        }

        return problems;
    }
}
