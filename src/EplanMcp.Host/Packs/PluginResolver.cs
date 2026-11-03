using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Packs;

namespace EplanMcp.Host.Packs;

/// <summary>One fresh state snapshot per resolution; binding documents remain resolved by the existing caller.</summary>
internal sealed class PluginResolver(PackLibrary packs)
{
    public Task<PluginResolution> ResolveAsync(CancellationToken cancellationToken) => ResolveAsync([], [], new ProjectBinding
    {
        BindingVersion = 2,
        Project = "global",
        Revision = 0,
        PackRoots = [],
        ProjectValues = new(),
        Cabinets = [new() { Id = "default", Layers = [], Values = new(), Structure = new() { ["all"] = true } }]
    }, 0, cancellationToken);

    public async Task<PluginResolution> ResolveAsync(IReadOnlyList<ConfigDocument> schemas, IReadOnlyList<ConfigDocument> layers,
        ProjectBinding binding, int cabinetIndex, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var plugins = new List<string>();
        var globalSchemas = new List<ConfigDocument>();
        var globalLayers = new List<ConfigDocument>();
        var skipped = new JsonArray();
        PluginState state = PluginState.Empty;
        try
        { (state, _) = await new PluginStateStore(packs.Locations).ReadAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (PluginConfigurationReader.Unreadable(ex))
        { warnings.Add("Plugin state is unreadable or invalid; global plugins are unavailable for this resolution."); }
        foreach (PluginIdentity plugin in state.Enabled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string identity = $"{plugin.Id}@{plugin.Version}";
            if (binding.Locks.Any(pin => pin.Id == plugin.Id))
            {
                string warning = $"{identity}: the project binding locks this plugin; its global configuration was skipped.";
                warnings.Add(warning);
                skipped.Add(new JsonObject { ["layer"] = "global", ["scope"] = "global", ["plugin"] = identity, ["kind"] = "warning", ["value"] = warning });
                continue;
            }
            var read = await new PluginConfigurationReader(packs).ReadAsync(plugin, cancellationToken).ConfigureAwait(false);
            warnings.AddRange(read.Warnings);
            globalSchemas.AddRange(read.Schemas);
            globalLayers.AddRange(read.Layers);
            if (read.Schemas.Count > 0 || read.Layers.Count > 0)
            { plugins.Add(identity); }
        }
        ConfigMergeResult merged = ConfigMerger.Merge([.. schemas, .. globalSchemas], layers, binding, cabinetIndex, globalLayers);
        if (!merged.Ok)
        { warnings.Add($"{merged.Reason} {merged.Location}: {merged.Message}"); plugins.Clear(); }
        var values = JsonNode.Parse(merged.Values.GetRawText())!.AsArray();
        foreach (JsonObject value in values.OfType<JsonObject>())
        {
            JsonNode[] sources = [.. value["trace"]!.AsArray().Where(item => item!["kind"]!.GetValue<string>() == "default").Select(item => item!)];
            value["winner"] = sources.LastOrDefault()?.DeepClone();
            value["overridden"] = new JsonArray(sources.Take(Math.Max(0, sources.Length - 1)).Select(source => source.DeepClone()).ToArray());
        }
        var trace = JsonNode.Parse(merged.Trace.GetRawText())!.AsArray();
        foreach (JsonNode? warning in skipped)
        { trace.Add(warning?.DeepClone()); }
        return new(JsonSerializer.SerializeToElement(values, PackJson.Options), plugins, warnings, JsonSerializer.SerializeToElement(trace, PackJson.Options));
    }
}
