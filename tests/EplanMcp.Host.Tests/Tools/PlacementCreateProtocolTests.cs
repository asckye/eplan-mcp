using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementCreateProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedCall_UsesOneWriteAndOneTransaction_WithMidBatchFailureRestoringEverything(bool failure)
    {
        await WithClient(async (client, harness, store) =>
        {
            store.Fail = failure;
            CallToolResult call = await client.CallToolAsync(PlacementCreateTools.Tool, Args(), cancellationToken: Token);
            Assert.Single(harness.Bridge.Writes);
            if (failure)
            {
                JsonElement error = WriteToolHarness.Error(call);
                Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
                Assert.Empty(store.Items);
                Assert.Equal(2, store.Writes);
                Assert.Equal(["Begin AI: eplan_placement_create", "Rollback", "Dispose"], store.Calls);
            }
            else
            {
                JsonElement data = WriteToolHarness.Structured(call);
                Assert.Equal(ExecutionState.Committed, data.GetProperty("execution").GetString());
                Assert.Equal(["text", "line", "rectangle"], store.Items.Select(i => i.Kind));
                Assert.Equal([15d, 25d], store.Items[0].Position);
                Assert.Equal([10d, 10d], store.Items[2].Position);
                Assert.Equal([40d, 40d], store.Items[2].EndPosition);
                Assert.Equal(3, data.GetProperty("items").GetArrayLength());
                Assert.Equal(["Begin AI: eplan_placement_create", "Commit", "CloseUndo", "Dispose"], store.Calls);
                Tool tool = Assert.Single(await client.ListToolsAsync(cancellationToken: Token), t => t.Name == PlacementCreateTools.Tool).ProtocolTool;
                Assert.True(PropertyGetContractTests.Accepts(tool.OutputSchema!.Value, data, tool.OutputSchema.Value));
                Assert.InRange(tool.Description!.Length + tool.InputSchema.GetRawText().Length, 1, 1000);
            }
        });
    }

    [Fact]
    public async Task MixedLanguageIndependentAndSpecificText_IsRejectedBeforeAnyBridgeCall()
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["items"] = JsonSerializer.Deserialize<JsonElement>("""[{"kind":"text","position":[10,10],"height_mm":3,"contents":{"??_??":"Panel","en_US":"Panel"}}]""");
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
            Assert.Contains("/items/0/contents", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
            Assert.Empty(harness.Bridge.Calls);
            Assert.Empty(store.Calls);
        });
    }

    [Fact]
    public async Task DryRunWithoutReliableTextOutline_ReportsCapabilityMissingWithoutWriting()
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["dry_run"] = true;
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(store.Calls);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutsideScratchPolicyOrStalePage_PreventsCreation(bool stale)
    {
        await WithClient(async (client, harness, store) =>
        {
            if (stale)
            { harness.Bridge.DuringWrite = _ => store.Geometry.GridSize = 2; }
            else
            { store.Project.Path = Path.Combine(harness.Locations.Root, "outside", "Line1"); }
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(PlacementCreateTools.Tool, Args(), cancellationToken: Token));
            Assert.Equal(stale ? ErrorReason.PlanStale : ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
            Assert.Empty(store.Items);
            Assert.Empty(store.Calls);
        });
    }

    private static Dictionary<string, object?> Args() => new()
    {
        ["project"] = "Line1",
        ["page"] = "P",
        ["items"] = JsonSerializer.Deserialize<JsonElement>("""
            [{"kind":"text","position":[12.5,22.5],"contents":{"??_??":"Panel"},"height_mm":3.5},
             {"kind":"line","points":[[10,10],[30,20]]},{"kind":"rectangle","corners":[[40,40],[10,10]]}]
            """),
    };

    private static async Task WithClient(Func<McpClient, WriteToolHarness, Store, Task> check)
    {
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        var store = new Store(harness.ProjectPath);
        harness.Bridge.Handlers[OperationNames.ProjectGet] = _ => new ProjectGetData { Project = store.Project };
        harness.Bridge.Handlers[OperationNames.PageGet] = _ => new PageGetData { Page = new() { Name = "P", Type = "Circuit" } };
        harness.Bridge.Handlers[OperationNames.PageDrawingGet] = _ => store.Geometry;
        var operation = new PlacementTextCreateExecuteOperation(store, store, store, store);
        harness.Bridge.Handlers[OperationNames.PlacementTextCreateExecute] = args =>
        {
            try
            { return operation.Execute(operation.ParseArgs(BridgeJson.FromObject(args)))!; }
            catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
        };
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(harness.Locations);
        builder.Services.AddSingleton(harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(Token);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: Token);
            await check(client, harness, store);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private sealed class Store(string projectPath) : IProjectCatalog, IPageDrawingReader, IPlacementTextWriter, ITransactionScopeFactory
    {
        public OpenProject Project { get; } = new() { Name = "Line1", Path = projectPath };
        public PageDrawingGeometry Geometry { get; } = new() { Page = "P", GridSize = 5, PageSize = [100, 100] };
        public List<PlacementText> Items { get; } = [];
        public List<string> Calls { get; } = [];
        public int Writes { get; private set; }
        public bool Fail { get; set; }
        public IReadOnlyList<OpenProject> ListOpenProjects() => [Project];
        public PageDrawingGeometry Read(string projectPath, string pageName) => Geometry;
        public void Validate(string projectPath, string page, PlacementText text) => Assert.Equal((Project.Path, "P"), (projectPath, page));
        public PlacementText Create(string projectPath, string page, PlacementText text)
        {
            Writes++;
            Items.Add(text);
            if (Fail && Writes == 2)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Forced mid-batch failure."); }
            return text;
        }
        public List<PlacementText> CreateBatch(string projectPath, string page, IReadOnlyList<PlacementText> items) => items.Select(item => Create(projectPath, page, item)).ToList();
        public ITransactionScope Begin(string undoDescription) { Calls.Add("Begin " + undoDescription); return new Scope(this, [.. Items]); }
        private sealed class Scope(Store store, PlacementText[] before) : ITransactionScope
        {
            public void Commit() => store.Calls.Add("Commit");
            public void CloseUndo() => store.Calls.Add("CloseUndo");
            public void Rollback() { store.Calls.Add("Rollback"); store.Items.Clear(); store.Items.AddRange(before); }
            public void Dispose() => store.Calls.Add("Dispose");
        }
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
