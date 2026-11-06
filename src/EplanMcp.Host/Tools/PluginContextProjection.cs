using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Tools;

/// <summary>Plugin text remains data; the ordinary context budget includes every complete summary.</summary>
internal static class PluginContextProjection
{
    internal static async Task AddAsync(PackLibrary packs, string? project, string? cabinet,
        Dictionary<string, List<(JsonObject Item, JsonObject Source)>> items, JsonObject result, CancellationToken cancellationToken)
    {
        PluginResolution resolution = await new PluginResolver(packs).ResolveAsync(project, cancellationToken, cabinet).ConfigureAwait(false);
        foreach (PluginDescriptor plugin in resolution.Enabled)
        {
            IReadOnlyList<AssetEntry> assets = plugin.Manifest is { } manifest
                ? await AssetIndex.ReadAsync(packs.Installed.FolderOf(plugin.Id, plugin.Version), manifest, cancellationToken).ConfigureAwait(false) : [];
            string identity = $"{plugin.Id}@{plugin.Version}";
            string[] keys = [.. (plugin.Manifest?.Provides ?? []).Concat(resolution.Values.EnumerateArray()
                .Where(value => value.GetProperty("trace").EnumerateArray().Any(trace => trace.TryGetProperty("source", out JsonElement source)
                    && source.TryGetProperty("resource", out JsonElement resource) && resource.GetString()!.StartsWith(identity + ":", StringComparison.Ordinal)))
                .Select(value => value.GetProperty("key").GetString()!))
                .Where(key => key.StartsWith("defaults.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            JsonElement[] defaults = [.. resolution.Values.EnumerateArray().Where(value => keys.Contains(value.GetProperty("key").GetString(), StringComparer.Ordinal))];
            items["plugins"].Add((ContextBuilder.Node(new
            {
                plugin.Id,
                plugin.Version,
                Kind = plugin.Manifest?.Kind.ToString().ToLowerInvariant(),
                plugin.Scope,
                Standards = plugin.Manifest?.Standards ?? [],
                ProvidedDefaults = keys,
                Defaults = defaults,
                RuleCount = assets.Count(entry => entry.Document is { Kind: KnowledgeKind.Rule, ApiVersion: KnowledgeDocument.V2ApiVersion }),
                Signoff = plugin.Manifest?.Signoff ?? new PackSignoff { Status = "draft" },
                Warnings = plugin.Warnings.Concat(assets.Where(entry => entry.DocumentProblem is not null)
                    .Select(entry => $"{identity}:{entry.Resource.Path}: {entry.DocumentProblem}"))
            }), new JsonObject()));
        }
        foreach (string warning in resolution.Warnings)
        { result["coverage"]!["missing"]!.AsArray().Add(warning); }
    }

    internal static async Task<JsonObject> OfflineAsync(PackLibrary packs, JsonElement scope, int tokens, CancellationToken cancellationToken)
    {
        var result = new JsonObject
        {
            ["project"] = null,
            ["cabinet"] = null,
            ["scope"] = JsonNode.Parse(scope.GetRawText()),
            ["contextRef"] = ContextBuilder.Reference(null, null),
            ["sources"] = new JsonArray(),
            ["coverage"] = ContextBuilder.Node(new { Profile = "offline", Format = "eplanmcp.plugins/1", Pages = Array.Empty<object>(), Missing = Array.Empty<string>() })
        };
        var items = ContextBuilder.Sections.ToDictionary(s => s, _ => new List<(JsonObject Item, JsonObject Source)>(), StringComparer.Ordinal);
        await AddAsync(packs, null, null, items, result, cancellationToken).ConfigureAwait(false);
        return ContextBuilder.Fit(ContextBuilder.Clean(result).AsObject(), items, ["plugins"], tokens);
    }
}
