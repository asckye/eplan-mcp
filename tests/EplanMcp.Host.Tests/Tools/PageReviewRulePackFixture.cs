using System.Security.Cryptography;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

internal sealed class PageReviewRulePackFixture : IDisposable
{
    internal const string Plugin = "review.fixture", Page = "=F+A1/1", Version = "1.0.0";
    internal static CancellationToken Cancel => TestContext.Current.CancellationToken;
    internal HostDataLocations Locations { get; } = new(Directory.CreateTempSubdirectory("eplanmcp-review-rules-").FullName);
    internal FakeBridgeClient Bridge { get; } = new();
    internal string ProjectPath { get; set; }
    internal JsonObject Evidence { get; set; }
    internal PackLibrary Packs { get; }

    internal PageReviewRulePackFixture()
    {
        ProjectPath = Path.Combine(Locations.DefaultScratch, "Line1");
        Packs = new(Locations, HostConfig.Defaults(Locations), new AuditLog(Locations.Audit, TimeProvider.System), TimeProvider.System);
        Evidence = CreateEvidence();
        Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = new() { Name = "Line1", Path = ProjectPath } };
        Bridge.Handlers[OperationNames.PageReview] = _ => Evidence.DeepClone().AsObject();
    }

    internal Task<CallToolResult> ReviewAsync(string[]? ids = null, string[]? checks = null, int limit = 50, string? cursor = null, bool useLibrary = true, string responseFormat = "concise") =>
        PageReviewTools.ReviewAsync(Bridge, TimeProvider.System, "Line1", Page, checks: checks, limit: limit, cursor: cursor,
            rule_packs: ids, packs: useLibrary ? Packs : null, response_format: responseFormat, cancellationToken: Cancel);

    internal async Task EnableAsync(params string[] ids)
    {
        var store = new PluginStateStore(Locations);
        (_, string? revision) = await store.ReadAsync(Cancel);
        await store.WriteAsync(new(1, ids.Select(id => new PluginIdentity(id, Version)).ToArray(), []), revision, Cancel);
    }

    internal void WritePack(string id = Plugin, bool isSigned = false, bool rules = true, IReadOnlyList<string>? standards = null)
    {
        string folder = Path.Combine(Locations.Packs, id, Version);
        Directory.CreateDirectory(folder);
        var resources = new List<PackResource>();
        if (rules)
        {
            Add("designation-letters", "mandatory", "function", """{"eq":[{"property":"function.main"},{"literal":true}]}""",
                """{"in":[{"property":"function.letters"},{"literal":["K","XQ"]}]}""", "标识字母不在夹具允许值中");
            Add("wire-colour", "recommended", "connection", """{"literal":true}""",
                """{"eq":[{"property":"connection.colour"},{"literal":"fixture-colour"}]}""", null);
        }
        var manifest = new PackManifest
        {
            SchemaVersion = 3,
            Id = id,
            Name = "Review fixture",
            Version = Version,
            Kind = PackKind.Standard,
            Standards = standards ?? ["Fixture standard:2026"],
            Compatibility = new(),
            Resources = resources,
            Signoff = isSigned ? new() { Status = "signed", SignedBy = "Fixture engineer", SignedAt = "2026-11-06" } : null,
            Provenance = new() { CreatedAt = DateTimeOffset.UnixEpoch, CreatedBy = "test", Inputs = [] },
        };
        Assert.Empty(PackManifestValidator.Validate(manifest));
        WriteManifest(folder, manifest);

        void Add(string name, string severity, string kind, string scope, string check, string? chinese)
        {
            var message = new JsonObject { ["en"] = "Fixture rule failed" };
            if (chinese is not null)
            { message["zh"] = chinese; }
            var document = new JsonObject
            {
                ["apiVersion"] = "eplanmcp.pack/v2",
                ["kind"] = "rule",
                ["metadata"] = new JsonObject { ["name"] = name },
                ["spec"] = new JsonObject
                {
                    ["severity"] = severity,
                    ["source"] = new JsonObject { ["standard"] = "Fixture standard:2026", ["clause"] = "1.2" },
                    ["scope"] = new JsonObject { ["kind"] = kind, ["where"] = JsonNode.Parse(scope) },
                    ["check"] = JsonNode.Parse(check),
                    ["message"] = message,
                    ["fix"] = JsonNode.Parse("""{"workflow":"workflow/unused","inputs":{"target":{"object":true}}}"""),
                },
            };
            string path = "knowledge/rule/" + name + ".json";
            byte[] content = PackJson.Serialize(document);
            string target = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            resources.Add(new() { Path = path, Kind = ResourceKind.Rule, Storage = ResourceStorage.Bundled, Bytes = content.Length, Files = 1, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
    }

    internal void MakeRulesLegacy(string id)
    {
        string folder = Path.Combine(Locations.Packs, id, Version);
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(folder, PackManifest.FileName)));
        var resources = new List<PackResource>();
        foreach (PackResource resource in manifest.Resources)
        {
            string original = Path.Combine(folder, resource.Path);
            JsonObject document = JsonNode.Parse(File.ReadAllText(original))!.AsObject();
            document["apiVersion"] = "eplanmcp.pack/v1";
            string path = resource.Path.Replace("knowledge/rule/", "resources/rules/", StringComparison.Ordinal);
            byte[] content = PackJson.Serialize(document);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, path))!);
            File.WriteAllBytes(Path.Combine(folder, path), content);
            File.Delete(original);
            resources.Add(resource with { Path = path, Bytes = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) });
        }
        WriteManifest(folder, manifest with { Resources = resources });
    }

    internal void MakeRuleUnavailable(string name, bool missing)
    {
        string folder = Path.Combine(Locations.Packs, Plugin, Version);
        string path = "knowledge/rule/" + name + ".json";
        if (missing)
        { File.Delete(Path.Combine(folder, path)); }
        else
        { ReplaceRule(folder, path, System.Text.Encoding.UTF8.GetBytes("{not-json")); }
    }

    private static void ReplaceRule(string folder, string path, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(folder, path), content);
        PackManifest manifest = PackJson.ReadManifest(File.ReadAllBytes(Path.Combine(folder, PackManifest.FileName)));
        WriteManifest(folder, manifest with
        {
            Resources = [.. manifest.Resources.Select(resource => resource.Path == path
                ? resource with { Bytes = content.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)) } : resource)],
        });
    }

    private static void WriteManifest(string folder, PackManifest manifest)
    {
        byte[] bytes = PackJson.Serialize(manifest);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.FileName), bytes);
        File.WriteAllBytes(Path.Combine(folder, PackManifest.LockFileName), PackJson.Serialize(PackLock.Create(new() { Id = manifest.Id, Version = Version, Digest = PackDigest.Compute(bytes) }, [])));
    }

    internal static JsonObject CreateEvidence(int count = 1, string? tag = "-K1", string? colour = "fixture-colour", bool main = true) => new()
    {
        ["project"] = "Line1",
        ["page"] = Page,
        ["issues"] = new JsonArray(),
        ["actual"] = new JsonObject
        {
            ["devices"] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)new JsonObject
            { ["tag"] = tag, ["category"] = "Fixture", ["main"] = main, ["position"] = new JsonArray(8, i * 8), ["locator"] = Locator(i + 1) }).ToArray()),
            ["connections"] = new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)new JsonObject
            { ["colour"] = colour, ["crossSection"] = "", ["potentialName"] = "", ["potentialType"] = "", ["label"] = "", ["position"] = new JsonArray(16, i * 8), ["locator"] = Locator(i + 101) }).ToArray()),
            ["interruptions"] = new JsonArray(),
            ["cables"] = new JsonArray(),
            ["texts"] = new JsonArray(),
            ["messages"] = new JsonArray(),
        },
    };

    internal static JsonObject Locator(int oid) => new()
    { ["project"] = "Line1", ["page"] = Page, ["type"] = 1, ["oid"] = oid, ["digest"] = new string('a', 64) };

    public void Dispose() => Directory.Delete(Locations.Root, recursive: true);
}
