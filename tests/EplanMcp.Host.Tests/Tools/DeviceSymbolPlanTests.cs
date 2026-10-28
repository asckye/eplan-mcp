using System.IO.Pipelines;
using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class DeviceSymbolPlanTests : IDisposable
{
    private const string Page = "=F+A1&EFS1/1";
    private readonly WriteToolHarness _harness = new();
    private readonly Store _store = new();
    private readonly Placement _placement;

    public DeviceSymbolPlanTests()
    {
        _placement = new Placement(_store);
        _store.Symbols.Add(new() { Library = "SPECIAL", Name = "SC", Variant = 0, Type = "LocationBox", Kind = "location_box" });
        Bind(_harness);
    }

    private void Bind(WriteToolHarness harness)
    {
        _store.Project.Path = harness.ProjectPath;
        harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = sent =>
        {
            try
            { return Plan((DevicePlaceArgs)sent!); }
            catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
        };
        harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = sent =>
        {
            try
            { return new DevicePlaceExecuteOperation(_store, _store, _store, _store, _store, null!, _store, (_, _, symbol) => { _placement.Resolved = symbol; return _placement; }).Execute(sent!)!; }
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
    public async Task Execution_ForwardsTheCompleteSymbolRequestAndCommitsOneStep_ThenRejectsRepeats()
    {
        JsonElement result = WriteToolHarness.Structured(await Place(dryRun: false));
        Assert.Equal(("committed", 1, 0, "AI: eplan_device_place"), (result.GetProperty("execution").GetString(), result.GetProperty("functions").GetInt32(), result.GetProperty("unplaced").GetInt32(), result.GetProperty("undo").GetString()));
        Assert.Equal("-K2", result.GetProperty("device").GetProperty("visibleTag").GetString());
        Assert.Equal(("IEC_symbol", "K", 0), (_placement.Resolved!.Library, _placement.Resolved.Name, _placement.Resolved.Variant));
        Assert.Equal((80, 160, "-K2"), (_placement.Args!.X, _placement.Args.Y, _placement.Args.Tag));
        Assert.Equal((20214, 4, -4), (_placement.Args.DisplayProperties![0].PropertyId, _placement.Args.DisplayProperties[0].OffsetXmm, _placement.Args.DisplayProperties[0].OffsetYmm));
        Assert.Equal(["A1", "A2"], _placement.Args.ConnectionDesignations!);
        Assert.Equal(["Begin", "Write", "Readback", "ReleaseLists", "Commit", "CloseUndo", "Dispose"], _placement.Calls);
        Assert.Equal(0, _store.PartReads);
        JsonElement again = WriteToolHarness.Error(await Place(dryRun: false));
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (again.GetProperty("reason").GetString(), again.GetProperty("execution").GetString()));
        Assert.Single(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Readback")]
    public async Task Execution_MidWriteOrReadbackMismatch_ReleasesListsBeforeAbortAndRestoresState(string failAt)
    {
        _placement.FailAt = failAt;
        JsonElement error = WriteToolHarness.Error(await Place(dryRun: false));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_store.Names);
        Assert.Equal(["page:1"], _store.Page.Objects);
        Assert.Equal(["ReleaseLists", "Abort", "Dispose"], _placement.Calls.TakeLast(3));
        Assert.DoesNotContain("CloseUndo", _placement.Calls);
        Assert.DoesNotContain("Commit", _placement.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Execution_OutsideScratch_BindsApprovalToSymbolDisplaysAndPins(bool grant)
    {
        var channel = new FakeApprovalChannel("eplan", grant ? ApprovalDecision.Granted : ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        CallToolResult result = await Place(dryRun: false, harness: harness);
        Assert.Equal(4, Assert.Single(channel.Questions).Plan.Changes.Count);
        Assert.Contains("20214 offset (4, -4)", channel.Questions[0].Plan.Changes[1].NewValue, StringComparison.Ordinal);
        if (grant)
        { Assert.Equal("committed", WriteToolHarness.Structured(result).GetProperty("execution").GetString()); }
        else
        {
            Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(result).GetProperty("reason").GetString());
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(_placement.Calls);
        }
    }

    [Fact]
    public async Task Execution_ApprovalChangesSymbolGeometry_RefusesBeforeTransaction()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Granted) { Wait = () => _store.Symbols[0].ConnectionPoints[0].Location[0] = 8 };
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        JsonElement error = WriteToolHarness.Error(await Place(dryRun: false, harness: harness));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_placement.Calls);
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

    private Task<CallToolResult> Place(bool dryRun = true, string[]? designations = null, string library = "IEC_symbol", string symbol = "K", int variant = 0, WriteToolHarness? harness = null) =>
        DeviceWriteTools.DevicePlaceAsync((harness ?? _harness).Pipeline, "Line1", Page, 80, 160, "-K2", dry_run: dryRun, library: library, symbol: symbol, symbol_variant: variant,
            display_properties: [JsonSerializer.SerializeToElement(new { property_id = 20214, offset_x_mm = 4, offset_y_mm = -4 })], connection_designations: designations ?? ["A1", "A2"], cancellationToken: TestContext.Current.CancellationToken);

    private WritePlan Plan(DevicePlaceArgs args) => (WritePlan)new DevicePlacePlanOperation(_store, _store, _store, _store, _store, _store).Execute(args)!;
    private static DevicePlaceArgs Args() => new() { Project = "Line1", Page = Page, Library = "IEC_symbol", Symbol = "K", SymbolVariant = 0, X = 80, Y = 160, Tag = "-K2" };

    private sealed class Placement(Store store) : IDeviceSymbolPlacement
    {
        public List<string> Calls { get; } = [];
        public string? FailAt { get; set; }
        public DevicePlaceArgs? Args { get; private set; }
        public SymbolSearchItem? Resolved { get; set; }
        public ITransactionScope Begin(string undoDescription)
        {
            Assert.Equal("AI: eplan_device_place", undoDescription);
            Calls.Add("Begin");
            return new Scope(this, store, [.. store.Names], [.. store.Page.Objects], [.. store.Page.LocationBoxStructures]);
        }
        public DevicePlaceData Place(DevicePlaceArgs args, string tag)
        {
            Args = args;
            bool createStrip = args.TerminalStrip is not null && !store.Names.Contains(tag[..tag.LastIndexOf(':')], StringComparer.OrdinalIgnoreCase);
            if (createStrip)
            { store.Names.Add(tag[..tag.LastIndexOf(':')]); }
            if (args.LocationBox is null)
            { store.Names.Add(tag); }
            else
            { store.Page.LocationBoxStructures.Add(args.LocationBox); }
            store.Page.Objects.Add("created");
            try
            {
                foreach (string stage in new[] { "Write", "Readback" })
                {
                    Calls.Add(stage);
                    if (FailAt == stage)
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Symbol readback differs; nothing was committed."); }
                }
                if (args.LocationBox is { } structure)
                {
                    var box = new LocationBoxData { Structure = structure, Bounds = [args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value] };
                    switch (FailAt)
                    {
                        case "Structure":
                            box.Structure = "+WRONG";
                            break;
                        case "Outline":
                            box.Outline = "solid";
                            break;
                        case "Bounds":
                            box.Bounds[2]++;
                            break;
                        case "Affected":
                            box.AffectedDevices = ["=F+A1-K1"];
                            break;
                    }
                    return new() { Tag = structure, Page = args.Page, Category = "LocationBox", LocationBox = box };
                }
                if (args.TerminalStrip is not null)
                {
                    string strip = tag[..tag.LastIndexOf(':')];
                    return new()
                    {
                        Tag = tag,
                        Page = args.Page,
                        Functions = createStrip ? 2 : 1,
                        Category = "Terminal",
                        Terminal = new()
                        {
                            Strip = strip,
                            Owner = FailAt == "Owner" ? "+A1-XOTHER" : strip, // a null owner is a relation pending commit (S43, live T7c)
                            Number = FailAt == "Number" ? "99" : args.TerminalNumber!,
                            Position = FailAt == "Position" ? [0, 0] : [args.X, args.Y],
                            StripCreated = createStrip,
                            SortCode = "17"
                        }
                    };
                }
                return new() { Tag = tag, VisibleTag = "-K2", Page = args.Page, Functions = 1, Category = "Coil" };
            }
            finally { Calls.Add("ReleaseLists"); }
        }
        private sealed class Scope(Placement owner, Store store, string[] names, string[] objects, string[] boxes) : ITransactionScope
        {
            public void Commit() => owner.Calls.Add("Commit");
            public void CloseUndo() => owner.Calls.Add("CloseUndo");
            public void Rollback()
            {
                Assert.Equal("ReleaseLists", owner.Calls[^1]);
                owner.Calls.Add("Abort");
                store.Names.Clear();
                store.Names.AddRange(names);
                store.Page.Objects = [.. objects];
                store.Page.LocationBoxStructures = [.. boxes];
            }
            public void Dispose() => owner.Calls.Add("Dispose");
        }
    }

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
