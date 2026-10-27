using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
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
public sealed class DeviceSymbolPlanTests : IDisposable
{
    private const string Page = "=F+A1&EFS1/1";
    private readonly WriteToolHarness _harness = new();
    private readonly Store _store = new();

    public DeviceSymbolPlanTests()
    {
        _store.Project.Path = _harness.ProjectPath;
        _harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = sent =>
        {
            try
            { return Plan((DevicePlaceArgs)sent!); }
            catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_ResolvesT1SymbolAndPredictsRelativePlacementsAndIndexedPins()
    {
        JsonElement result = WriteToolHarness.Structured(await Place());
        Assert.Equal(("not_started", 1, 0, "", ""), (result.GetProperty("execution").GetString(), result.GetProperty("functions").GetInt32(), result.GetProperty("unplaced").GetInt32(), result.GetProperty("part").GetString(), result.GetProperty("variant").GetString()));
        Assert.Equal("=F+A1-K2", result.GetProperty("device").GetProperty("tag").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("device").GetProperty("visibleTag").ValueKind);
        Assert.Equal(("IEC_symbol", "K", 0), (result.GetProperty("symbol").GetProperty("library").GetString(), result.GetProperty("symbol").GetProperty("name").GetString(), result.GetProperty("symbol").GetProperty("variant").GetInt32()));
        Assert.Equal(4, result.GetProperty("changes").GetArrayLength());
        Assert.Equal("20214 offset (4, -4), page (84, 156)", result.GetProperty("changes")[1].GetProperty("newValue").GetString());
        Assert.Equal("20022[1]", result.GetProperty("changes")[2].GetProperty("property").GetString());
        Assert.Equal("A1", result.GetProperty("changes")[2].GetProperty("newValue").GetString());
        Assert.Equal("A2", result.GetProperty("changes")[3].GetProperty("newValue").GetString());
        Assert.Equal(("IEC_symbol", "K"), (_store.Search!.Library, _store.Search.NameContains));
        Assert.Equal(0, _store.PartReads);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Equal(OperationNames.DevicePlacePlan, Assert.Single(_harness.Bridge.Calls).Operation);
    }

    [Theory]
    [InlineData("=F+A1-k2")]
    [InlineData("=F+A1-K2:13")]
    public async Task DryRun_ExistingNamesAndPinSuffixesAreConflicts(string existing)
    {
        _store.Names.Add(existing);
        JsonElement error = WriteToolHarness.Error(await Place());
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task DryRun_DesignationsMustMatchThePinCount(int count)
    {
        JsonElement error = WriteToolHarness.Error(await Place(designations: [.. Enumerable.Repeat("1", count)]));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("/connection_designations", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Missing", "K", 0)]
    [InlineData("IEC_symbol", "K_missing", 0)]
    [InlineData("IEC_symbol", "K", 99)]
    public async Task DryRun_MissingExactSourceIsSymbolNotFound(string library, string symbol, int variant)
    {
        JsonElement error = WriteToolHarness.Error(await Place(library: library, symbol: symbol, variant: variant));
        Assert.Equal(ErrorReason.SymbolNotFound, error.GetProperty("reason").GetString());
        Assert.Contains("eplan_symbol_search", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRun_UsesUniqueCaseInsensitiveSourceButRejectsAmbiguousNames()
    {
        Assert.Equal("K", WriteToolHarness.Structured(await Place(library: "iec_symbol", symbol: "k")).GetProperty("symbol").GetProperty("name").GetString());
        _store.Symbols.Add(new() { Library = "IEC_symbol", Name = "k", Variant = 0, Type = "Function", Kind = "function" });
        JsonElement error = WriteToolHarness.Error(await Place(library: "iec_symbol", symbol: "k"));
        Assert.Equal(ErrorReason.AmbiguousName, error.GetProperty("reason").GetString());
        Assert.Contains("IEC_symbol/K", error.GetProperty("metadata").GetProperty("candidates").GetString(), StringComparison.Ordinal);
        Assert.False((await Place()).IsError is true);
    }

    [Fact]
    public async Task DryRun_RejectsSpecializedSymbolsAndInvalidGeometry()
    {
        _store.Symbols[0].Kind = "terminal";
        Assert.Equal(ErrorReason.CapabilityMissing, WriteToolHarness.Error(await Place()).GetProperty("reason").GetString());
        _store.Symbols[0].Kind = "function";
        _store.Symbols[0].ConnectionPoints[0].Location = [double.NaN, 0];
        Assert.Equal(ErrorReason.EplanOperationFailed, WriteToolHarness.Error(await Place()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task DryRun_RetainsCircuitPageAndBoundaryChecks()
    {
        _store.Page.Type = "Overview";
        _store.Page.Width = 50;
        JsonElement error = WriteToolHarness.Error(await Place());
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("Circuit", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("must lie on the page", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execution_IsCapabilityMissingBeforeApprovalOrAnyBridgeCall()
    {
        JsonElement error = WriteToolHarness.Error(await Place(dryRun: false));
        Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal("device.place.symbol.execute", error.GetProperty("metadata").GetProperty("missing").GetString());
        Assert.Empty(_harness.Bridge.Calls);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public void BridgeExecution_AlsoRejectsSymbolsWithoutEnteringThePartPath()
    {
        var operation = new DevicePlaceExecuteOperation(_store, _store, _store, _store, _store, null!);
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => operation.Execute(Args()));
        Assert.Equal((ErrorReason.CapabilityMissing, ExecutionState.NotStarted), (ex.Error.Reason, ex.Error.Execution));
        Assert.Equal(0, _store.PartReads);
    }

    [Fact]
    public void Plan_ProducesReviewableApprovalLinesAndBindsGeometryAndPlacements()
    {
        DevicePlaceArgs args = Args();
        args.DisplayProperties = [new() { PropertyId = 20214, OffsetXmm = 4, OffsetYmm = -4 }];
        args.ConnectionDesignations = ["A1", "A2"];
        WritePlan plan = Plan(args);
        Assert.Equal(
        [
            "create function =F+A1&EFS1/1 (IEC_symbol/K, variant 0 as =F+A1-K2 at (80, 160); no part)",
            "create property_placement =F+A1-K2 (20214 offset (4, -4), page (84, 156))",
            "update pin =F+A1-K2: 20022[1] '' -> 'A1'",
            "update pin =F+A1-K2: 20022[2] '' -> 'A2'",
        ], WritePipeline.ChangeLines(plan));
        _store.Symbols[0].ConnectionPoints[0].Location[0] = 4;
        Assert.NotEqual(plan.Fingerprint, Plan(args).Fingerprint);
        string geometryFingerprint = Plan(args).Fingerprint;
        args.DisplayProperties[0].OffsetXmm = 8;
        Assert.NotEqual(geometryFingerprint, Plan(args).Fingerprint);
        string placementFingerprint = Plan(args).Fingerprint;
        args.ConnectionDesignations[0] = "13";
        Assert.NotEqual(placementFingerprint, Plan(args).Fingerprint);
    }

    [Fact]
    public async Task McpCall_RetainsStrictRawDisplayValidationAndSymbolDryRun()
    {
        var incoming = new Pipe();
        var outgoing = new Pipe();
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_harness.Locations);
        builder.Services.AddSingleton(_harness.Pipeline);
        builder.Services.AddEplanMcp().WithStreamServerTransport(incoming.Reader.AsStream(), outgoing.Writer.AsStream());
        builder.Services.AddSingleton<IBridgeClient>(_harness.Bridge);
        using IHost host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await using McpClient client = await McpClient.CreateAsync(new StreamClientTransport(incoming.Writer.AsStream(), outgoing.Reader.AsStream()), cancellationToken: TestContext.Current.CancellationToken);
            var args = new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["page"] = Page,
                ["x"] = 80,
                ["y"] = 160,
                ["tag"] = "-K2",
                ["library"] = "IEC_symbol",
                ["symbol"] = "K",
                ["symbol_variant"] = 0,
                ["dry_run"] = true,
            };
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync(DeviceWriteTools.PlaceTool, args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("K", result.GetProperty("symbol").GetProperty("name").GetString());
            int reads = _harness.Bridge.Invocations;
            foreach (string raw in new[]
            {
                """[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4,"offset_y_mm":5}]""",
                """[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4,"extra":5}]""",
            })
            {
                using JsonDocument document = JsonDocument.Parse(raw);
                args["display_properties"] = document.RootElement.Clone();
                JsonElement error = WriteToolHarness.Error(await client.CallToolAsync(DeviceWriteTools.PlaceTool, args, cancellationToken: TestContext.Current.CancellationToken));
                Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
                Assert.Contains("/display_properties/0:", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            Assert.Equal(reads, _harness.Bridge.Invocations);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private Task<CallToolResult> Place(bool dryRun = true, string[]? designations = null, string library = "IEC_symbol", string symbol = "K", int variant = 0) =>
        DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, "Line1", Page, 80, 160, "-K2", dry_run: dryRun, library: library, symbol: symbol, symbol_variant: variant,
            display_properties: [JsonSerializer.SerializeToElement(new { property_id = 20214, offset_x_mm = 4, offset_y_mm = -4 })], connection_designations: designations ?? ["A1", "A2"], cancellationToken: TestContext.Current.CancellationToken);

    private WritePlan Plan(DevicePlaceArgs args) => (WritePlan)new DevicePlacePlanOperation(_store, _store, _store, _store, _store, _store).Execute(args)!;
    private static DevicePlaceArgs Args() => new() { Project = "Line1", Page = Page, Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, X = 80, Y = 160, Tag = "-K2" };

    private sealed class Store : IProjectCatalog, IPageReader, IDeviceRenamer, IPartsDatabase, IDevicePlacer, ISymbolReader
    {
        public OpenProject Project { get; } = new() { Name = "Line1" };
        public MacroTargetPage Page { get; } = new() { Name = DeviceSymbolPlanTests.Page, Type = "Circuit", Width = 420, Height = 297, Objects = ["page:1"] };
        public List<string> Names { get; } = [];
        public List<SymbolSearchItem> Symbols { get; } = [new() { Library = "IEC_symbol", Name = "K", Variant = 0, Type = "Function", Kind = "function", ConnectionPoints = [new() { Location = [0, 0], Direction = "Up" }, new() { Location = [0, -4], Direction = "Down" }] }];
        public SymbolSearchArgs? Search { get; private set; }
        public int PartReads { get; private set; }
        public IReadOnlyCollection<string> PageTypes => ["Circuit"];
        public IReadOnlyList<OpenProject> ListOpenProjects() => [Project];
        public IReadOnlyList<string> PageNames(string projectPath) => [Page.Name];
        public IReadOnlyList<string> FunctionNames(string projectPath) => Names;
        public MacroTargetPage? ReadPage(string projectPath, string pageName) => pageName == Page.Name ? Page : null;
        public List<SymbolSearchItem> Read(string projectPath, SymbolSearchArgs args) { Search = args; return Symbols; }
        public PartsDatabaseContent Read(string? database) { PartReads++; throw new NotSupportedException(); }
        public DeviceNames? ReadDevice(string projectPath, string tag) => throw new NotSupportedException();
        public RenameReadBack Rename(string projectPath, string tag, string newTag) => throw new NotSupportedException();
        public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language) => throw new NotSupportedException();
        public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language) => throw new NotSupportedException();
        public PlacedDevice Place(string projectPath, string pageName, string part, string variant, double x, double y, string tag) => throw new NotSupportedException();
    }
}
