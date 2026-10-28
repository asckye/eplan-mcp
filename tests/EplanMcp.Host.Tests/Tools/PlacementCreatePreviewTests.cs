using System.Text.Encodings.Web;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementCreatePreviewTests
{
    private static readonly JsonSerializerOptions _json = new(McpJsonUtilities.DefaultOptions)
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task StagedContractAndUsage_MatchReviewedSnapshotWithoutRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(HostDataLocations.FromEnvironment()));
        services.AddEplanMcp();
        await using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.DoesNotContain(options.ToolCollection!, t => t.ProtocolTool.Name == PlacementCreateContract.Definition.Name);
        Assert.DoesNotContain(PlacementCreateContract.Definition.Name, ToolUsageCatalog.Usage.Keys);
        DirectoryInfo root = new(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "EplanMcp.Ci.slnf")))
        { root = root.Parent!; }
        string usage = await File.ReadAllTextAsync(Path.Combine(root.FullName, "src/EplanMcp.Host/Tools/PlacementCreate.usage.md"), TestContext.Current.CancellationToken);
        IReadOnlyDictionary<string, string> topics = ToolUsageCatalog.Parse("PlacementCreate.usage.md", usage);
        Assert.Equal(ToolUsageCatalog.Topics, topics.Keys);
        JsonElement example = JsonDocument.Parse(topics[ToolUsageCatalog.Examples].Split("```json\n", StringSplitOptions.None)[1].Split("```", StringSplitOptions.None)[0]).RootElement;
        Assert.Empty(PlacementCreatePlanning.Validate(example));
        Assert.True(PropertyGetContractTests.Accepts(PlacementCreateContract.Input, example, PlacementCreateContract.Input));
        Assert.Empty(PlacementCreatePlanning.Validate(PlacementCreateTests.Input(PlacementCreateTests.Text + "," + PlacementCreateTests.Line + "," + PlacementCreateTests.Rectangle)));
        await Verify(JsonSerializer.Serialize(new { profile = "full", tool = PlacementCreateContract.Definition, usage = usage.ReplaceLineEndings("\n") }, _json) + "\n", "json")
            .UseDirectory("../../Contracts.Snapshots").UseFileName("placement-create-contract");
    }

    [Fact]
    public async Task MixedDryRun_ReportsCompleteImpactAndNeverInvokesWrite()
    {
        FakeBridgeClient bridge = PlacementCreateTests.Bridge();
        JsonElement data = PlacementCreateTests.Data(await PlacementCreateTests.Preview(bridge,
            PlacementCreateTests.Input(PlacementCreateTests.Text + "," + PlacementCreateTests.Line + "," + PlacementCreateTests.Rectangle)));
        Assert.Equal([OperationNames.ProjectGet, OperationNames.PageGet, OperationNames.PageDrawingGet], bridge.Calls.Select(c => c.Operation));
        Assert.Empty(bridge.Writes);
        Assert.All(bridge.Calls.Skip(1), c => Assert.Equal(("Line1", "P"), (((PageGetArgs)c.Args!).Project, ((PageGetArgs)c.Args!).Page)));
        Assert.Equal(3, data.GetProperty("changes").GetArrayLength());
        Assert.Equal(3, data.GetProperty("items").GetArrayLength());
        Assert.Empty(data.GetProperty("connectionChanges").EnumerateArray());
        Assert.Equal(ExecutionState.NotStarted, data.GetProperty("execution").GetString());
        Assert.All(data.GetProperty("changes").EnumerateArray(), c => Assert.Contains("bounds", c.GetString(), StringComparison.Ordinal));
        await Verify(JsonSerializer.Serialize(data, _json) + "\n", "json")
            .UseDirectory("../../Contracts.Snapshots").UseFileName("placement-create-preview");
    }
}
