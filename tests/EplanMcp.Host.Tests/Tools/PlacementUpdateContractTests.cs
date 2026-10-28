using System.Text.Encodings.Web;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementUpdateContractTests
{
    private static readonly JsonSerializerOptions _json = new(McpJsonUtilities.DefaultOptions) { WriteIndented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    [Fact]
    public async Task ContractAndUsage_RegisteredWithClosedSchema()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()));
        services.AddEplanMcp();
        await using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.Contains(options.ToolCollection!, t => t.ProtocolTool.Name == PlacementUpdateContract.Definition.Name);
        Assert.Contains(PlacementUpdateContract.Definition.Name, ToolUsageCatalog.Usage.Keys);
        DirectoryInfo root = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "EplanMcp.Ci.slnf")))
        { root = root.Parent!; }
        string usage = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src/EplanMcp.Host/Tools/PlacementUpdate.usage.md"), TestContext.Current.CancellationToken);
        Assert.Equal(ToolUsageCatalog.Topics, ToolUsageCatalog.Parse("PlacementUpdate.usage.md", usage).Keys);
        JsonElement example = JsonDocument.Parse(usage.ReplaceLineEndings("\n").Split("```json\n", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0]).RootElement;
        Assert.True(PropertyGetContractTests.Accepts(PlacementUpdateContract.Input, example, PlacementUpdateContract.Input));
        Assert.Equal((false, false, true, false), (PlacementUpdateContract.Definition.Annotations!.ReadOnlyHint, PlacementUpdateContract.Definition.Annotations.DestructiveHint, PlacementUpdateContract.Definition.Annotations.IdempotentHint, PlacementUpdateContract.Definition.Annotations.OpenWorldHint));
        var scene = new ConnectionCreatePlanner.Scene
        {
            Complete = true,
            Project = new() { Name = "Line1", Path = "scratch-project" },
            Page = new() { Project = "scratch-project", Page = "P", GridSize = 5, PageSize = [100, 100] },
        };
        var placement = new EplanMcp.Contracts.DrawingPlacement
        {
            Locator = new() { Project = "scratch-project", Page = "P", Type = 4, Oid = 1, Digest = new string('a', 64) },
            Kind = "Text",
            Location = [20, 20],
            Bounds = [20, 20, 25, 25],
            Properties = new() { ["grouped"] = "false" },
        };
        scene.Page.Placements.Add(placement);
        var request = new JObject { ["project"] = "Line1", ["page"] = "P", ["dry_run"] = true, ["moves"] = new JArray(new JObject { ["locator"] = BridgeJson.FromObject(placement.Locator), ["x_mm"] = 30, ["y_mm"] = 30 }) };
        foreach (bool delta in new[] { false, true })
        {
            if (delta)
            { request["moves"]![0]!["x_mm"]!.Parent!.Remove(); request["moves"]![0]!["y_mm"]!.Parent!.Remove(); request["moves"]![0]!["delta_mm"] = new JArray(10, 10); }
            JsonElement input = JsonDocument.Parse(request.ToString()).RootElement;
            Assert.True(PropertyGetContractTests.Accepts(PlacementUpdateContract.Input, input, PlacementUpdateContract.Input));
            JsonElement result = JsonDocument.Parse(PlacementMovePlanner.Plan(request.ToString(), scene).ToString()).RootElement;
            JsonElement output = PlacementUpdateContract.Definition.OutputSchema!.Value;
            Assert.True(PropertyGetContractTests.Accepts(output, result, output));
        }
        await Verify(JsonSerializer.Serialize(new { registered = true, tool = PlacementUpdateContract.Definition, usage = usage.ReplaceLineEndings("\n") }, _json) + "\n", "json")
            .UseDirectory("../../Contracts.Snapshots").UseFileName("placement-update-contract");
    }
}
