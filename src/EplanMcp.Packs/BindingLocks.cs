namespace EplanMcp.Packs;

/// <summary>Resolves only frozen installed locks; the caller verifies the installed files, never selects versions.</summary>
public static class BindingLocks
{
    public static async Task<IReadOnlyList<PackReference>> ResolveAsync(ProjectBinding binding,
        Func<string, string, CancellationToken, Task<(PackManifest Manifest, PackLock Lock)>> read, CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<string, (PackManifest Manifest, PackLock Lock, string Pointer)>(StringComparer.Ordinal);
        var closures = new Dictionary<string, HashSet<PackReference>>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach ((PackDependency root, int index) in binding.PackRoots!.Select((r, i) => (r, i)))
        {
            _ = await VisitAsync(root.Id, root.Version, $"/packRoots/{index}").ConfigureAwait(false);
        }
        foreach ((CabinetBinding cabinet, int index) in binding.Cabinets.Select((c, i) => (c, i)))
        {
            foreach ((string layer, int position) in cabinet.Layers!.Select((l, i) => (l, i)))
            {
                string at = $"/cabinets/{index}/layers/{position}";
                BindingReadModel.Require(AssetNames.TryParse(layer, out string? id, out string? version, out string? local), at, "must name an exact config layer");
                _ = await VisitAsync(id!, version!, at).ConfigureAwait(false);
                BindingReadModel.Require(loaded[id!].Manifest.Resources.Any(r => r.Kind == ResourceKind.ConfigLayer && AssetNames.Local(r) == local), at, "config layer is not in the locked pack");
            }
        }
        return [.. loaded.Values.Select(p => p.Lock.Pack).OrderBy(p => p.Id, StringComparer.Ordinal)];

        async Task<HashSet<PackReference>> VisitAsync(string id, string version, string at)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BindingReadModel.Require(!visiting.Contains(id), at, "pack dependencies form a cycle");
            if (loaded.TryGetValue(id, out var previous))
            {
                BindingReadModel.Require(previous.Lock.Pack.Version == version, at, $"'{id}' requires {version}, conflicting with {previous.Lock.Pack.Version} at {previous.Pointer}");
                return closures[id];
            }
            (PackManifest manifest, PackLock frozen) = await read(id, version, cancellationToken).ConfigureAwait(false);
            BindingReadModel.Require(manifest.Id == id && manifest.Version == version && frozen.Problems(manifest, frozen.Pack.Digest).Count == 0, at, "installed lock does not match its manifest");
            loaded.Add(id, (manifest, frozen, at));
            visiting.Add(id);
            var closure = new HashSet<PackReference>();
            foreach (PackDependency dependency in manifest.Dependencies)
            {
                PackReference? pin = frozen.Dependencies.FirstOrDefault(p => p.Id == dependency.Id);
                BindingReadModel.Require(pin is not null && VersionRange.TryParse(dependency.Version, out VersionRange? range)
                    && range.Contains(SemanticVersion.Parse(pin.Version)), at, $"'{dependency.Id}' has no frozen version satisfying {dependency.Version}");
                HashSet<PackReference> children = await VisitAsync(pin!.Id, pin.Version, at).ConfigureAwait(false);
                BindingReadModel.Require(loaded[pin.Id].Lock.Pack == pin, at, $"'{pin.Id}' has a conflicting frozen digest");
                closure.UnionWith(children);
            }
            BindingReadModel.Require(closure.SetEquals(frozen.Dependencies), at, "pack.lock dependencies are not the exact transitive closure");
            closure.Add(frozen.Pack);
            closures.Add(id, closure);
            visiting.Remove(id);
            return closure;
        }
    }
}
