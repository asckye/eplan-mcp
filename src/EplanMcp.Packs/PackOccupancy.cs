namespace EplanMcp.Packs;

/// <summary>All versions referenced by bindings and their frozen closures; never chooses newer versions.</summary>
public static class PackOccupancy
{
    public static async Task<IReadOnlySet<(string Id, string Version)>> ResolveAsync(IEnumerable<ProjectBinding> bindings,
        Func<string, string, CancellationToken, Task<(PackManifest Manifest, PackLock Lock)>> read, CancellationToken cancellationToken)
    {
        var occupied = new HashSet<(string Id, string Version)>();
        foreach (ProjectBinding binding in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach ((PackReference pin, int index) in binding.Locks.Select((p, i) => (p, i)))
            {
                BindingReadModel.Require(PackManifestValidator.IsId(pin.Id) && SemanticVersion.TryParse(pin.Version, out _)
                    && PackManifestValidator.IsSha256(pin.Digest), $"/locks/{index}", "must contain an exact pack version and SHA-256");
            }
            // Legacy bindings may pin only a root; even locks no longer listed as v2 roots still protect versions.
            ProjectBinding roots = binding with
            {
                PackRoots = [.. binding.Locks.Select(p => new PackDependency { Id = p.Id, Version = p.Version }), .. binding.PackRoots ?? []],
                Cabinets = binding.BindingVersion == 1 ? [] : binding.Cabinets,
            };
            IReadOnlyList<PackReference> closure = await BindingLocks.ResolveAsync(roots, read, cancellationToken).ConfigureAwait(false);
            foreach ((PackReference pin, int index) in binding.Locks.Select((p, i) => (p, i)))
            {
                BindingReadModel.Require(closure.Contains(pin), $"/locks/{index}", "does not match the frozen installed version; cleanup is blocked");
            }
            occupied.UnionWith(closure.Select(p => (p.Id, p.Version)));
        }
        return occupied;
    }
}
