using System.Security;
using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>Reads only explicitly configured resources of an exact installed plugin; gaps remain warnings.</summary>
internal sealed class PluginConfigurationReader(PackLibrary packs)
{
    public async Task<(IReadOnlyList<ConfigDocument> Schemas, IReadOnlyList<ConfigDocument> Layers, IReadOnlyList<string> Warnings, PackManifest? Manifest)> ReadAsync(
        PluginIdentity plugin, CancellationToken cancellationToken)
    {
        var schemas = new Dictionary<string, ConfigDocument>(StringComparer.Ordinal);
        var layers = new List<ConfigDocument>();
        var warnings = new List<string>();
        string identity = $"{plugin.Id}@{plugin.Version}";
        PackManifest? available = null;
        try
        {
            string folder = new PlanPaths(packs.Locations.Root).Check(packs.Installed.FolderOf(plugin.Id, plugin.Version));
            (PackManifest? manifest, _, IReadOnlyList<PackProblem> problems) = await PackVerifier.ReadManifestAsync(folder, cancellationToken).ConfigureAwait(false);
            if (manifest is null || problems.Count > 0 || manifest.Id != plugin.Id || manifest.Version != plugin.Version)
            { throw new PackFormatException("The exact installed manifest is unavailable or invalid."); }
            available = manifest;
            var loaded = new Dictionary<string, ConfigDocument?>(StringComparer.Ordinal);
            IEnumerable<string> references = (manifest.Configuration.Base?.Resources ?? []).Concat(manifest.Configuration.Company?.Resources ?? [])
                .Concat(manifest.Configuration.CabinetTypes.SelectMany(scope => scope.Resources));
            foreach (string path in references.Distinct(PackPath.Comparer))
            {
                PackResource? resource = manifest.Resources.FirstOrDefault(item => PackPath.Comparer.Equals(item.Path, path));
                if (resource is null || resource.Kind is not ResourceKind.ConfigSchema and not ResourceKind.ConfigLayer)
                { continue; }
                ConfigDocument? document = await ReadResourceAsync(resource).ConfigureAwait(false);
                if (document is null)
                { continue; }
                if (resource.Kind == ResourceKind.ConfigSchema)
                { schemas.TryAdd(document.Resource, document); continue; }
                string? reference = document.Spec.TryGetProperty("schema", out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
                PackResource? schemaResource = manifest.Resources.FirstOrDefault(item => item.Kind == ResourceKind.ConfigSchema && AssetNames.Local(item) == reference);
                ConfigDocument? schema = schemaResource is null ? null : await ReadResourceAsync(schemaResource).ConfigureAwait(false);
                try
                {
                    ConfigValidator.Layer(document.Spec, _ => schema?.Spec);
                    schemas.TryAdd(schema!.Resource, schema);
                    layers.Add(document);
                }
                catch (PackFormatException)
                { warnings.Add($"{document.Resource}: configuration layer or its referenced schema is unavailable or invalid."); }
            }

            async Task<ConfigDocument?> ReadResourceAsync(PackResource resource)
            {
                if (loaded.TryGetValue(resource.Path, out ConfigDocument? previous))
                { return previous; }
                loaded[resource.Path] = null;
                try
                {
                    _ = new PlanPaths(packs.Locations.Root).Check(Path.Combine(folder, resource.Path.Replace('/', Path.DirectorySeparatorChar)));
                    if (await PackVerifier.ResourceProblemAsync(folder, resource, packs.Config.PackSources, cancellationToken).ConfigureAwait(false) is not null)
                    { throw new PackFormatException("Configuration content is unavailable or changed."); }
                    AssetEntry entry = (await AssetIndex.ReadAsync(folder, manifest with { Resources = [resource] }, cancellationToken).ConfigureAwait(false)).Single();
                    if (entry.Document is not { } document)
                    { throw new PackFormatException("Configuration document cannot be read."); }
                    JsonElement spec = JsonSerializer.SerializeToElement(document.Spec, PackJson.Options);
                    if (resource.Kind == ResourceKind.ConfigSchema)
                    { ConfigValidator.Schema(spec); }
                    return loaded[resource.Path] = new(entry.Id, resource.Sha256, spec);
                }
                catch (Exception ex) when (Unreadable(ex))
                { warnings.Add($"{identity}:{AssetNames.Local(resource)}: configuration content is missing, unreadable or invalid."); return null; }
            }
        }
        catch (Exception ex) when (Unreadable(ex))
        { warnings.Add($"{identity}: exact installed plugin data is missing, unreadable or invalid."); }
        return ([.. schemas.Values], layers, warnings, available);
    }

    internal static bool Unreadable(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException or PackFormatException;
}
