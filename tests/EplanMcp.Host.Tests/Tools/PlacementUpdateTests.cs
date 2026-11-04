using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlacementUpdateTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("batch")]
    [InlineData("stale")]
    [InlineData("noop")]
    [InlineData("dry")]
    [InlineData("policy")]
    [InlineData("property_success")]
    [InlineData("property_style")]
    [InlineData("property_batch")]
    [InlineData("property_stale")]
    [InlineData("property_noop")]
    [InlineData("property_dry")]
    public async Task RegisteredTool_UsesExistingWritePipelineAndAtomicMoveBranch(string scenario)
    {
        bool style = scenario == "property_style";
        if (style)
        { scenario = "property_success"; }
        bool property = scenario.StartsWith("property_", StringComparison.Ordinal);
        scenario = scenario.Replace("property_", "", StringComparison.Ordinal);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Deny);
        var fake = new Mover(harness.ProjectPath, property) { Fail = scenario == "batch" };
        if (scenario == "policy")
        { fake.Scene.Project.Path = Path.Combine(harness.Locations.Root, "outside", "Line1"); fake.Scene.Page.Project = fake.Scene.Project.Path; }
        var planner = new ConnectionCreatePlanOperation(fake);
        var execute = new ConnectionCreateExecuteOperation(fake, null!, fake, fake);
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = args => Invoke(planner, args!);
        harness.Bridge.Handlers[OperationNames.ConnectionCreateExecute] = args => Invoke(execute, args!);
        if (scenario == "stale")
        { harness.Bridge.DuringWrite = _ => fake.Scene.Page.GridSize = 2; }
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
            var args = new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["page"] = "P",
                ["dry_run"] = scenario == "dry",
                ["moves"] = new[] { new { locator = fake.Scene.Page.Placements[0].Locator, x_mm = scenario == "noop" ? 20 : 30, y_mm = 20 } }
            };
            int target = property ? 1 : 0;
            if (property)
            {
                args.Remove("moves");
                args["property_moves"] = new[] { new { parent = fake.Scene.Page.Placements[0].Locator, property_id = 20214, property_index = 0, x_mm = scenario == "noop" ? 20 : 30, y_mm = 20, angle_deg = scenario == "noop" ? 0 : 90 } };
            }
            if (style)
            {
                args["property_moves"] = new[] { new { parent = fake.Scene.Page.Placements[0].Locator, property_id = 20214, property_index = 0, x_mm = 30, y_mm = 20, angle_deg = 90,
                    alignment = "MiddleCenter", height_mm = -16002, visible = false, layer = 3, pen = new { color = -16002, width_mm = 0.25, style = 1, pattern_length = 8, line_end = 1 } } };
            }
            CallToolResult result = await client.CallToolAsync(PlacementUpdateTools.Tool, args, cancellationToken: Token);
            if (scenario is "batch" or "stale" or "policy")
            {
                JsonElement error = WriteToolHarness.Error(result);
                Assert.Equal(scenario == "batch" ? ErrorReason.EplanOperationFailed : scenario == "stale" ? ErrorReason.PlanStale : ErrorReason.PolicyDenied, error.GetProperty("reason").GetString());
                Assert.Equal(scenario == "batch" ? ExecutionState.RolledBack : ExecutionState.NotStarted, error.GetProperty("execution").GetString());
                Assert.Equal([20d, 20d], fake.Scene.Page.Placements[target].Location);
            }
            else
            {
                JsonElement data = WriteToolHarness.Structured(result);
                Assert.Equal(scenario == "success" ? ExecutionState.Committed : ExecutionState.NotStarted, data.GetProperty("execution").GetString());
                Assert.Equal(scenario == "success" ? [30d, 20d] : [20d, 20d], fake.Scene.Page.Placements[target].Location);
                JsonElement schema = PlacementUpdateContract.Definition.OutputSchema!.Value;
                Assert.True(PropertyGetContractTests.Accepts(schema, data, schema));
            }
            Assert.Equal(scenario is "success" or "batch" or "stale" ? 1 : 0, harness.Bridge.Writes.Count);
            Assert.Equal(scenario is "success" or "batch" ? 1 : 0, fake.Transactions);
            Assert.Equal(scenario == "success" ? 1 : 0, fake.Closed);
            if (style)
            {
                Assert.Equal("success", scenario);
                Assert.Equal("MiddleCenter", fake.Scene.Page.Placements[1].Properties["alignment"]);
                Assert.Equal("from_layer", fake.Scene.Page.Placements[1].Properties["height_mm"]);
                Assert.Equal("-16002", fake.Scene.Page.Placements[1].Properties["height_raw_mm"]);
            }
            if (property)
            { Assert.Equal([20d, 20d], fake.Scene.Page.Placements[0].Location); }
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static object Invoke(IOperation operation, object args)
    {
        try
        {
            object data = operation.Execute(operation.ParseArgs(JObject.Parse(JsonSerializer.Serialize(args, HostJson.Options))))!;
            return data is JObject obj ? JsonNode.Parse(obj.ToString())!.AsObject() : data;
        }
        catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
    }
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class Mover : IConnectionCreator, IPlacementMover, ITransactionScopeFactory
    {
        public ConnectionCreatePlanner.Scene Scene { get; private set; }
        public bool Fail { get; init; }
        public JObject Interruptions(string project, IReadOnlyList<string> pages) => new() { ["pages"] = new JArray(), ["points"] = new JArray() };
        public ITransactionScopeFactory PlacementTransactions(string project, bool undoCommitted = false) => this;
        public int Transactions { get; private set; }
        public int Closed { get; private set; }
        public Mover(string path, bool property = false)
        {
            Scene = new() { Complete = true, Project = new() { Name = "Line1", Path = path }, Page = new() { Project = path, Page = "P", GridSize = 5, PageSize = [100, 100] } };
            Scene.Page.Placements.Add(new() { Kind = "Text", Locator = new() { Project = path, Page = "P", Type = 4, Oid = 1, Digest = new string('a', 64) }, Location = [20, 20], Bounds = [20, 20, 25, 25], Properties = new() { ["grouped"] = "false", ["text"] = "??_??@Panel;" } });
            if (property)
            { Scene.Page.Placements.Add(new() { Kind = "PropertyPlacement", Locator = new() { Project = path, Page = "P", Type = 4, Oid = 1, Digest = new string('b', 64) }, Parent = Scene.Page.Placements[0].Locator, Location = [20, 20], Bounds = [20, 20, 25, 25], Properties = new() { ["grouped"] = "false", ["property_id"] = "20214", ["property_index"] = "0", ["angle_deg"] = "0", ["docked"] = "false" } }); }
        }
        public ConnectionCreatePlanner.Scene Read(string project, string page) => Clone(Scene);
        public Action Prepare(ConnectionCreatePlanner.Scene scene, JArray moves) => () =>
        {
            DrawingPlacement target = Scene.Page.Placements.Single(p => p.Locator.Digest == (string)moves[0]!["locator"]!["digest"]!);
            target.Location = moves[0]!["after"]!.Values<double>().ToArray();
            target.Bounds = moves[0]!["bounds"]!.Values<double>().ToArray();
            if (moves[0]!["angle"] is { } angle)
            { target.Properties["angle_deg"] = angle.ToString(); target.Properties["docked"] = "false"; }
            if (moves[0]!["style"] is JObject style)
            {
                foreach (JProperty field in style.Properties())
                {
                    if (field.Name == "pen")
                    { foreach (JProperty p in ((JObject)field.Value).Properties()) { target.Properties["pen_" + p.Name] = p.Value.ToString(); } }
                    else if (field.Name == "height_mm")
                    { target.Properties["height_raw_mm"] = field.Value.ToString(); target.Properties["height_mm"] = (double)field.Value == -16002 ? "from_layer" : field.Value.ToString(); }
                    else
                    { target.Properties[field.Name] = field.Value.Type == JTokenType.Boolean ? field.Value.ToString().ToLowerInvariant() : field.Value.ToString(); }
                }
            }
            if (Fail)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Injected failure after moving the first item."); }
        };
        public ITransactionScope Begin(string undoDescription) { Assert.Equal("AI: eplan_placement_update", undoDescription); Transactions++; return new Scope(this, Clone(Scene)); }
        private sealed class Scope(Mover mover, ConnectionCreatePlanner.Scene before) : ITransactionScope
        {
            public void Commit() { }
            public void CloseUndo() => mover.Closed++;
            public void Rollback() => mover.Scene = before;
            public void Dispose() { }
        }
        private static ConnectionCreatePlanner.Scene Clone(ConnectionCreatePlanner.Scene scene) => BridgeJson.FromObject(scene).ToObject<ConnectionCreatePlanner.Scene>(BridgeJson.Serializer)!;
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) => throw new NotSupportedException();
        public LayoutGuard.Model Measure(string project, string page) => LayoutGuard.From(Read(project, page));
        public Action Prepare(string project, string page, JArray symbols, Action<LayoutGuard.Model>? verifyLayout = null) => throw new NotSupportedException();
        public List<string?[]> Endpoints(string project, string page) => throw new NotSupportedException();
        public void Undo(string project) => throw new NotSupportedException();
    }
}
