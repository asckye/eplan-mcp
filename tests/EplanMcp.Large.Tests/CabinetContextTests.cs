using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Host;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

/// <summary>Reads the existing sample; only the temporary Host workspace is seeded, never the EPLAN project.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class CabinetContextTests(LiveEplan eplan)
{
    [Fact]
    public async Task MultiCabinetListAndContext_KeepProjectFilesUnchanged()
    {
        string root = Directory.CreateTempSubdirectory("eplanmcp-cabinet-large-").FullName;
        try
        {
            CancellationToken token = TestContext.Current.CancellationToken;
            await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
            Dictionary<string, string> before = LiveSnapshots.DesignFiles(eplan.ProjectPath);
            var locations = new HostDataLocations(root);
            var packs = new PackLibrary(locations, HostConfig.Defaults(locations), new AuditLog(locations.Audit, TimeProvider.System), TimeProvider.System);
            string project = PlanSnapshotHashes.ProjectKey(eplan.ProjectPath);
            var binding = new ProjectBinding
            {
                BindingVersion = 2,
                Project = project,
                Revision = 1,
                PackRoots = [],
                ProjectValues = new(),
                Cabinets = [Cabinet("a1", "+A1"), Cabinet("a2", "+A2")]
            };
            string folder = Directory.CreateDirectory(packs.Workspaces.FolderOf(project)).FullName;
            string file = Path.Combine(folder, ProjectBinding.FileName);
            byte[] bytes = PackJson.Serialize(binding);
            await File.WriteAllBytesAsync(file, bytes, token);
            CallToolResult listed = await CabinetTools.ListAsync(packs, bridge, project, cancellationToken: token);
            Assert.False(listed.IsError == true);
            Assert.Equal(2, listed.StructuredContent!.Value.GetProperty("total").GetInt32());
            foreach (CabinetBinding cabinet in binding.Cabinets)
            {
                CallToolResult call = await ContextTools.GetAsync(bridge, packs, JsonSerializer.SerializeToElement(new { }), project, cabinet.Id, max_tokens: 20000, cancellationToken: token);
                Assert.False(call.IsError == true, string.Join("\n", call.Content.OfType<TextContentBlock>().Select(c => c.Text)));
                JsonElement result = call.StructuredContent!.Value;
                Assert.Equal(cabinet.Id, result.GetProperty("cabinet").GetString());
                Assert.NotEmpty(result.GetProperty("coverage").GetProperty("pages").EnumerateArray());
                var resolver = new CabinetScopeResolver(binding.Cabinets);
                Assert.All(result.GetProperty("coverage").GetProperty("pages").EnumerateArray(), p => Assert.Equal([cabinet.Id], resolver.CabinetOf(p.GetProperty("name").GetString())));
                Assert.Empty(result.GetProperty("config").EnumerateArray());
            }
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file, token));
            Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(eplan.ProjectPath).OrderBy(p => p.Key, StringComparer.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static CabinetBinding Cabinet(string id, string location) => new()
    {
        Id = id,
        Layers = [],
        Values = new(),
        Structure = new() { ["prefixes"] = new JsonArray(new JsonObject { ["location"] = location }) }
    };
}
