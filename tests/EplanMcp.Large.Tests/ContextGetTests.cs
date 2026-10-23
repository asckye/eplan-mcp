using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>P24 read-only sample-fixture evidence: page hashes and source identities against project.snapshot.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class ContextGetTests(LiveEplan eplan)
{
    [Fact]
    public async Task P24_ContextGet_PerPageHashesAndSourcesMatchProjectSnapshot()
    {
        string root = Directory.CreateTempSubdirectory("eplanmcp-context-large-").FullName;
        try
        {
            await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            Dictionary<string, string> before = LiveSnapshots.DesignFiles(eplan.ProjectPath);
            LiveSnapshots.Snapshot snapshot = await LiveSnapshots.TakeAsync(bridge, new()
            {
                Project = eplan.ProjectPath
            });
            var locations = new HostDataLocations(root);
            var packs = new PackLibrary(locations, HostConfig.Defaults(locations), new AuditLog(locations.Audit, TimeProvider.System), TimeProvider.System);
            int compared = 0;
            foreach (SnapshotPage page in snapshot.Pages.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                JsonElement scope = JsonSerializer.SerializeToElement(new
                {
                    pages = new List<string> { page.Name }
                });
                CallToolResult call = await ContextTools.GetAsync(bridge, packs, scope, project: eplan.ProjectPath,
                    include: JsonSerializer.SerializeToElement(new List<string> { "objects", "facts" }), max_tokens: 20000, cancellationToken: TestContext.Current.CancellationToken);
                Assert.False(call.IsError == true, string.Join("\n", call.Content.OfType<TextContentBlock>().Select(b => b.Text)));
                JsonElement context = call.StructuredContent!.Value;
                Assert.Equal(JsonValueKind.Null, context.GetProperty("contextRef").GetProperty("snapshotHash").ValueKind);
                Assert.Equal(JsonValueKind.Null, context.GetProperty("contextRef").GetProperty("generation").ValueKind);
                JsonElement covered = Assert.Single(context.GetProperty("coverage").GetProperty("pages").EnumerateArray());
                Assert.Equal(page.Name, covered.GetProperty("name").GetString());
                Assert.Equal(page.Hash, covered.GetProperty("hash").GetString());
                Dictionary<string, JsonElement> sources = context.GetProperty("sources").EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!, StringComparer.Ordinal);
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement source in sources.Values)
                {
                    Assert.Equal(PlanSnapshotHashes.ProjectKey(eplan.ProjectPath), source.GetProperty("project").GetString());
                    SnapshotObject obj = Assert.Single(page.Objects, o => o.Kind == source.GetProperty("kind").GetString()
                        && o.Type == source.GetProperty("type").GetInt32() && o.Oid == source.GetProperty("oid").GetInt64());
                    Assert.Equal(obj.Hash, source.GetProperty("hash").GetString());
                    Assert.Equal(page.Hash, source.GetProperty("pageHash").GetString());
                    Assert.Equal(SnapshotProfiles.Format, source.GetProperty("format").GetString());
                    compared++;
                }
                foreach (JsonElement item in context.GetProperty("objects").EnumerateArray().Concat(context.GetProperty("facts").EnumerateArray()))
                {
                    string id = Assert.Single(item.GetProperty("sources").EnumerateArray()).GetString()!;
                    used.Add(id);
                    JsonElement locator = item.GetProperty("locator");
                    JsonElement source = sources[id];
                    Assert.Equal(locator.GetProperty("kind").GetString(), source.GetProperty("kind").GetString());
                    Assert.Equal(locator.GetProperty("type").GetInt32(), source.GetProperty("type").GetInt32());
                    Assert.Equal(locator.GetProperty("oid").GetInt64(), source.GetProperty("oid").GetInt64());
                    if (item.TryGetProperty("hash", out JsonElement hash))
                    {
                        Assert.Equal(source.GetProperty("hash").GetString(), hash.GetString());
                    }
                }
                Assert.Equal(sources.Keys.Order(StringComparer.Ordinal), used.Order(StringComparer.Ordinal));
                TestContext.Current.TestOutputHelper?.WriteLine($"{page.Name}: hash={page.Hash}, {sources.Count} sources, omitted={context.GetProperty("budget").GetProperty("omittedItems").GetInt32()}");
            }
            Assert.True(compared > 0);
            Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(eplan.ProjectPath).OrderBy(p => p.Key, StringComparer.Ordinal));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
