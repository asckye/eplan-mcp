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
public sealed partial class PlacementCreateProtocolTests
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
                Assert.InRange(tool.Description!.Length + PageDisplay.Description.Length, 1, 1000);
                Assert.InRange(tool.InputSchema.GetRawText().Length, 1, 1000);
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
    public async Task DryRunWithoutReliableTextOutline_DefersMeasurementWithoutWriting()
    {
        await WithClient(async (client, harness, store) =>
        {
            store.Measure = false;
            Dictionary<string, object?> args = Args();
            args["dry_run"] = true;
            CallToolResult result = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.NotEqual(true, result.IsError);
            JsonElement preview = WriteToolHarness.Structured(result);
            Assert.Equal(JsonValueKind.Null, preview.GetProperty("items")[0].GetProperty("bounds").ValueKind);
            Assert.Contains("unknown bounds deferred", preview.GetProperty("warning").GetString(), StringComparison.Ordinal);
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

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewKinds_UseAllThreeDryRunPathsAndExactBridgeRequests(bool? dryRun)
    {
        await WithClient(async (client, harness, store) =>
        {
            string folder = Directory.CreateDirectory(Path.Combine(harness.Locations.Imports, "graphics")).FullName;
            await File.WriteAllBytesAsync(Path.Combine(folder, "test.png"), [137, 80, 78, 71], Token);
            Dictionary<string, object?> args = Args();
            if (dryRun.HasValue)
            { args["dry_run"] = dryRun.Value; }
            else
            { args.Remove("dry_run"); }
            args["items"] = JsonSerializer.Deserialize<JsonElement>("[" + PlacementGraphicsTests.Graphics + "]");
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            bool preview = dryRun != false;
            Assert.Equal(preview, result.GetProperty("dryRun").GetBoolean());
            Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
            PlacementTextCreateArgs request = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
            Assert.Equal(["polyline", "arc", "path_text", "image"], request.Items!.Select(i => i.Kind));
            Assert.Equal([10d, 10d], request.Items![0].Position);
            Assert.Equal(3, request.Items![0].Points.Length);
            Assert.Equal([20d, 15d], request.Items![0].Points[1]);
            Assert.Equal([30d, 10d], request.Items![0].Points[2]);
            Assert.Equal([50d, 50d], request.Items![1].Position);
            Assert.Equal((5d, 30d, 120d), (request.Items![1].RadiusMm, request.Items![1].StartAngle, request.Items![1].EndAngle));
            Assert.Equal([60d, 70d], request.Items![2].Position);
            Assert.Equal(3.5, request.Items![2].HeightMm);
            Assert.Equal("Function", request.Items![2].Contents["??_??"]);
            Assert.Equal([70d, 10d], request.Items![3].Position);
            Assert.Equal([80d, 20d], request.Items![3].EndPosition);
            Assert.Equal("graphics/test.png", request.Items![3].File);
            Assert.Equal(Path.Combine(folder, "test.png"), request.Items![3].FilePath);
            Assert.Equal(64, request.Items![3].Sha256.Length);
            Assert.Equal(preview ? 0 : 1, harness.Bridge.Writes.Count);
            Assert.Equal(preview ? 0 : 4, store.Items.Count);
            if (!preview)
            {
                PlacementTextCreateArgs executed = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreateExecute).Args);
                Assert.Equal(JsonSerializer.Serialize(request.Items), JsonSerializer.Serialize(executed.Items));
            }
        });
    }

    [Theory]
    [InlineData("Overview", true)]
    [InlineData("Graphics", true)]
    [InlineData("TableOfContents", false)]
    public async Task DrawingPageKinds_ExecuteAllowedTypesAndRejectOthers(string type, bool allowed)
    {
        await WithClient(async (client, harness, store) =>
        {
            harness.Bridge.Handlers[OperationNames.PageGet] = _ => new PageGetData { Page = new() { Name = "P", Type = type } };
            CallToolResult result = await client.CallToolAsync(PlacementCreateTools.Tool, Args(), cancellationToken: Token);
            Assert.Equal(!allowed, result.IsError == true);
            Assert.Equal(allowed ? 3 : 0, store.Items.Count);
            if (!allowed)
            { Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(result).GetProperty("reason").GetString()); }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PathText_OmittedHeightReachesPreviewAndWriteAsDefaultSentinel(bool preview)
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["dry_run"] = preview;
            args["items"] = JsonSerializer.Deserialize<JsonElement>("""[{"kind":"path_text","position":[20,20],"contents":{"??_??":"Path text"}}]""");
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
            PlacementTextCreateArgs request = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
            Assert.Equal(-16002, request.Items![0].HeightMm);
            Assert.Equal(preview ? 0 : 1, store.Items.Count);
        });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Ellipse_ProtocolPreservesAxesAndChecksGeometry(bool preview, bool valid)
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["dry_run"] = preview;
            args["items"] = new[] { new { kind = "ellipse", center = (double[])[50d, 50d], radius_x_mm = valid ? 4 : 0, radius_y_mm = 8, angle = 90 } };
            CallToolResult call = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.Equal(!valid, call.IsError == true);
            if (valid)
            {
                PlacementTextCreateArgs request = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
                Assert.Equal((4d, 8d, 90d), (request.Items![0].RadiusXmm, request.Items[0].RadiusYmm, request.Items[0].Angle));
                Assert.Equal(preview ? 0 : 1, store.Items.Count);
            }
            else
            { Assert.Empty(store.Items); Assert.Empty(harness.Bridge.Calls); }
        });
    }

    [Theory]
    [InlineData("\"height_mm\":-16002,\"contents\":{\"??_??\":\"Path text\"}")]
    [InlineData("\"contents\":{}")]
    public async Task PathText_InvalidHeightOrMissingTextRejectsBeforeRead(string fields)
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["items"] = JsonSerializer.Deserialize<JsonElement>("[{\"kind\":\"path_text\",\"position\":[20,20]," + fields + "}]");
            JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token));
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Empty(harness.Bridge.Calls);
            Assert.Empty(store.Items);
        });
    }

    private static Dictionary<string, object?> Args() => new()
    {
        ["project"] = "Line1",
        ["page"] = "P",
        ["dry_run"] = false,
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
        double[]? Measure(PlacementText text) => store.Measure ? [text.Position[0], text.Position[1], text.Position[0] + 8, text.Position[1] + 3.5] : null;
        var operation = new PlacementTextCreateExecuteOperation(store, store, store, store, measureText: Measure);
        var preflight = new PlacementTextCreateExecuteOperation(store, store, store, store, measureText: Measure, planning: true);
        harness.Bridge.Handlers[OperationNames.PlacementTextCreatePlan] = args =>
        {
            try
            { return preflight.Execute(preflight.ParseArgs(BridgeJson.FromObject(args)))!; }
            catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
        };
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
        public bool Measure { get; set; } = true;
        public string? DisplayLanguage { get; set; }
        public IReadOnlyList<OpenProject> ListOpenProjects() => [Project];
        public PageDrawingGeometry Read(string projectPath, string pageName) => Geometry;
        public string? Validate(string projectPath, string page, PlacementText text, bool warn = false)
        {
            Assert.Equal((Project.Path, "P"), (projectPath, page));
            if (DisplayLanguage is not null && text.Kind is "text" or "path_text" && !text.Contents.ContainsKey("??_??") && !text.Contents.ContainsKey(DisplayLanguage))
            {
                const string message = "/text/contents must include the project display language or ??_?? (A54).";
                if (warn)
                { return message; }
                throw new BridgeErrorException(ErrorReason.InvalidArgument, message);
            }
            return null;
        }
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
