using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

internal sealed partial class PluginResolver
{
    public async Task<PluginResolution> ResolveAsync(string? project, CancellationToken cancellationToken, string? cabinet = null, string? page = null)
    {
        if (project is null)
        { return await ResolveAsync(cancellationToken).ConfigureAwait(false); }
        var warnings = new List<string>();
        var enabled = new List<PluginDescriptor>();
        var documents = new Dictionary<string, ConfigDocument>(StringComparer.Ordinal);
        ProjectBinding? binding = null;
        var paths = new PlanPaths(packs.Locations.Root);
        string key = PlanSnapshotHashes.ProjectKey(project);
        try
        {
            _ = paths.Check(Path.Combine(packs.Workspaces.FolderOf(key), ProjectBinding.FileName));
            binding = await packs.Workspaces.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            if (binding is not null && PlanSnapshotHashes.ProjectKey(binding.Project) != key)
            { throw new PackFormatException("Binding project identity does not match."); }
        }
        catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
        { binding = null; warnings.Add("Project binding is unreadable or invalid; bound plugin data is unavailable."); }
        if (binding is null)
        {
            PluginResolution global = await ResolveAsync(cancellationToken).ConfigureAwait(false);
            return global with { Warnings = [.. global.Warnings, .. warnings], BindingProject = key };
        }
        CabinetBinding? selected = binding.Cabinets.SingleOrDefault(c => c.Id == cabinet);
        selected ??= binding.Cabinets.Count == 1 ? binding.Cabinets[0] : null;
        if (selected is null && page is not null)
        {
            IReadOnlyList<string> matches = new CabinetScopeResolver(binding.Cabinets).CabinetOf(page);
            selected = matches.Count == 1 ? binding.Cabinets.Single(c => c.Id == matches[0]) : null;
        }
        if (selected is null && binding.Cabinets.Count > 0)
        { warnings.Add("No unique cabinet was selected; cabinet plugin configuration is unavailable."); }
        selected ??= new() { Id = "default", Layers = [], Values = new(), Structure = new() { ["all"] = true } };
        IReadOnlyList<PackReference> selectedPins = [];
        try
        {
            if (binding.BindingVersion == 2)
            {
                selectedPins = await BindingLocks.ResolveAsync(binding with { Cabinets = [selected] }, async (id, version, token) =>
                {
                    PackCheckResult check = await PackVerifier.CheckDirectoryAsync(paths.Check(packs.Installed.FolderOf(id, version)), token).ConfigureAwait(false);
                    if (!check.Ok || check.Manifest is not { } manifest || check.Lock is not { } frozen || !binding.Locks.Contains(frozen.Pack))
                    { throw new PackFormatException("The selected cabinet's exact closure is unavailable."); }
                    return (manifest, frozen);
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
        { warnings.Add("The selected cabinet's exact closure is unavailable; bound configuration was not applied."); }
        foreach (PackReference pin in binding.Locks)
        {
            PackManifest? manifest = null;
            var gaps = new List<string>();
            try
            {
                string folder = paths.Check(packs.Installed.FolderOf(pin.Id, pin.Version));
                var read = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
                if (read.Manifest is null || read.Problems.Count > 0 || read.Digest != pin.Digest
                    || read.Manifest.Id != pin.Id || read.Manifest.Version != pin.Version)
                { throw new PackFormatException("The exact bound manifest is unavailable or changed."); }
                manifest = read.Manifest;
                if (!selectedPins.Contains(pin))
                { enabled.Add(new(pin.Id, pin.Version, "project", manifest, gaps)); continue; }
                foreach (AssetEntry entry in await AssetIndex.ReadAsync(folder, manifest, cancellationToken).ConfigureAwait(false))
                {
                    if (entry.Resource.Kind != ResourceKind.ConfigSchema && !(selected.Layers ?? []).Contains(entry.Id, StringComparer.Ordinal))
                    { continue; }
                    _ = paths.Check(Path.Combine(folder, entry.Resource.Path.Replace('/', Path.DirectorySeparatorChar)));
                    if (entry.Document is not { } document || await PackVerifier.ResourceProblemAsync(folder, entry.Resource, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) is not null)
                    { gaps.Add($"{entry.Id}: bound configuration content is missing, unreadable or invalid."); continue; }
                    documents[entry.Id] = new(entry.Id, entry.Resource.Sha256, JsonSerializer.SerializeToElement(document.Spec, PackJson.Options));
                }
            }
            catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
            { gaps.Add($"{pin.Id}@{pin.Version}: exact bound plugin data is missing, unreadable or invalid."); }
            enabled.Add(new(pin.Id, pin.Version, (selected.Layers ?? []).Any(id => id.StartsWith($"{pin.Id}@{pin.Version}:", StringComparison.Ordinal)) ? "cabinet" : "project", manifest, gaps));
            warnings.AddRange(gaps);
        }
        string[] layers = [.. selected.Layers ?? []];
        foreach (string missing in layers.Where(id => !documents.ContainsKey(id)))
        { warnings.Add($"{missing}: bound configuration layer is unavailable."); }
        ProjectBinding mergedBinding = binding.BindingVersion == 2 ? binding with { Cabinets = [selected] } : new()
        {
            BindingVersion = 2,
            Project = binding.Project,
            Revision = binding.Revision,
            Locks = binding.Locks,
            PackRoots = [],
            ProjectValues = new(),
            Cabinets = [selected with { Layers = [], Values = new(), Structure = new() { ["all"] = true } }]
        };
        PluginResolution result = await ResolveAsync([.. documents.Values.Where(d => d.Resource.Contains(":config_schema/", StringComparison.Ordinal))],
            [.. layers.Where(documents.ContainsKey).Select(id => documents[id])], mergedBinding, 0, cancellationToken).ConfigureAwait(false);
        return result with { Enabled = [.. result.Enabled, .. enabled], Warnings = [.. result.Warnings, .. warnings], BindingProject = key, BindingRevision = binding.Revision };
    }
}
