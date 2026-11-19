using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EplanMcp.Host;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Tests;

internal static class PageReviewLayoutPluginFixture
{
    internal const string Id = "review.layout-fixture";
    internal const string Version = "1.0.0";

    internal static async Task InstallAsync(HostDataLocations locations, JsonObject defaults, CancellationToken token)
    {
        const string resource = "knowledge/config_schema/layout.json";
        string folder = Path.Combine(locations.Packs, Id, Version);
        string target = Path.Combine(folder, resource);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var items = new JsonObject();
        foreach ((string key, JsonNode? value) in defaults)
        {
            items[key] = new JsonObject
            {
                ["type"] = value!.GetValueKind() == System.Text.Json.JsonValueKind.String ? "text" : "number",
                ["required"] = false,
                ["allowedLayers"] = new JsonArray("base", "company", "project", "cabinet"),
                ["default"] = value.DeepClone(),
            };
        }
        byte[] content = PackJson.Serialize(new { apiVersion = "eplanmcp.pack/v2", kind = "config_schema", metadata = new { name = "layout" }, spec = new { items } });
        File.WriteAllBytes(target, content);
        var manifest = new PackManifest
        {
            SchemaVersion = 3,
            Id = Id,
            Name = "Page review thresholds",
            Version = Version,
            Compatibility = new(),
            Resources = [new() { Path = resource, Kind = ResourceKind.ConfigSchema, Storage = ResourceStorage.Bundled, Files = 1, Bytes = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) }],
            Configuration = new() { Company = new() { Id = "layout", Resources = [resource] } },
            Provenance = new() { CreatedAt = DateTimeOffset.UnixEpoch, CreatedBy = "test", Inputs = [] },
        };
        byte[] bytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        var pin = new PackReference { Id = Id, Version = Version, Digest = PackDigest.Compute(bytes) };
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(pin, [])));
        var store = new PluginStateStore(locations);
        (PluginState state, string? revision) = await store.ReadAsync(token);
        await store.WriteAsync(state with { Enabled = [.. state.Enabled, new(Id, Version)] }, revision, token);
    }
}
