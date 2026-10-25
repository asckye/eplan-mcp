using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Bridge;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanStepCompilerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    [InlineData("device.update")]
    [InlineData("macro.insert")]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public async Task Compile_SevenExistingPlanCalls_UseToolDtosAndCompleteImpacts(string op)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        PlanStep step = await new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input(op), Token);

        Assert.Equal(op + ".plan", fake.Calls[0].Op);
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(ExpectedArgs(op), HostJson.Options), fake.Calls[0].Args));
        Assert.True(step.Effective);
        Assert.Equal(PlanStepCatalog.Tier(op), step.Tier);
        Assert.NotEmpty(step.PlannedChanges);
        Assert.NotEmpty(step.Expected);
        Assert.DoesNotContain("fingerprint", PlanCanonical.Serialize(PlanTestData.Definition() with { Steps = [step] }), StringComparison.OrdinalIgnoreCase);
        Assert.All(fake.Calls, call => Assert.DoesNotContain("execute", call.Op, StringComparison.Ordinal));
        if (op == "device.update")
        {
            Assert.Equal(["+A1/1", "+A1/2"], step.ModifyScope.Pages);
            Assert.Equal([11L, 12], step.ModifyScope.Locators.Select(l => l.Oid));
            Assert.Equal(["+A1-K1"], fake.Calls[1].Args["names"]!.AsArray().Select(n => n!.GetValue<string>()));
            Assert.Equal(PlanScopeRules.Whole(baseline.Snapshot).Pages, step.ReadScope.Pages);
            Assert.All(step.Expected, e => Assert.Equal("+A1-K9", e.GetProperty("value").GetProperty("name").GetString()));
        }
        else if (op == "page.delete")
        {
            Assert.Equal(["+A1/1", "+A1/2"], step.ModifyScope.Pages);
            Assert.Equal([1L, 11, 12], step.ModifyScope.Locators.Select(l => l.Oid));
            Assert.Equal(["+A1/1", "+A1/2"], step.ReadScope.Pages);
            Assert.Contains(step.Expected, e => e.GetProperty("tag").GetString() == "orphaned");
            string preview = PlanDiffPreview.For([step]);
            Assert.Contains("Page +A1/1\n    delete page +A1/1", preview, StringComparison.Ordinal);
            Assert.Contains("Page +A1/2\n    update device +A1-K1: main function", preview, StringComparison.Ordinal);
        }
        else if (op is "page.renumber" or "device.renumber")
        {
            Assert.Equal(["+A1/1", "+A1/2", "+A10/1"], step.ModifyScope.Pages);
            Assert.Equal(step.ReadScope.Pages, step.ModifyScope.Pages);
            Assert.False(Assert.Single(step.Expected).GetProperty("value").GetProperty("exactNames").GetBoolean());
        }
        else
        {
            Assert.Single(step.ModifyScope.Pages);
            Assert.Equal(op == "page.update" ? 1 : 3, step.ReadScope.Pages.Length);
        }
        Assert.Equal(op is "device.update" or "page.delete" ? 2 : 1, fake.Calls.Count);
    }

    [Fact]
    public async Task Compile_PageDescriptionNoOp_KeepsResolvedNameAndDoesNotCreateAnyWrite()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        fake.Plans["page.update"].Changes.Clear();
        PlanArgumentStep input = PlanStepCompilationData.Input("page.update", """{"pages":[{"name":"+a1/1","description":"Old"}]}""");
        PlanStep step = await new PlanStepCompiler(fake.Client).CompileAsync(baseline, input, Token);
        Assert.False(step.Effective);
        Assert.Equal("+A1/1", Assert.Single(step.ModifyScope.Locators).Name);
        Assert.Equal("Old", Assert.Single(step.Expected).GetProperty("value").GetProperty("description").GetString());
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task Compile_NumberingNoOp_KeepsWholeProjectScopeAndNoEffectiveChange()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        fake.Plans["device.renumber"].Changes.Clear();
        PlanStep step = await new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("device.renumber"), Token);
        Assert.False(step.Effective);
        Assert.Equal(["+A1/1", "+A1/2", "+A10/1"], step.ModifyScope.Pages);
        Assert.Empty(step.PlannedChanges);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task Compile_NullAndEmptyOptionalValues_UseTheSameDtosAsTools()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        PlanArgumentStep input = PlanStepCompilationData.Input("page.create", """{"pages":[{"name":"+A1/3","description":""}]}""");
        await new PlanStepCompiler(fake.Client).CompileAsync(baseline, input, Token);
        PageCreateArgs args = fake.Calls[0].Args.Deserialize<PageCreateArgs>(HostJson.Options)!;
        Assert.Equal("en_US", args.Language);
        Assert.Equal("Circuit", Assert.Single(args.Pages).PageType);
        Assert.Null(args.Pages[0].Description);
        Assert.Null(args.Fingerprint);
    }

    [Fact]
    public async Task Compile_DeviceClosureLongerThanPreview_IsCompleteInExpectedAndPreview()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(11, 61)
            .Select(i => PlanScopeTestData.Function(i, i == 11 ? "+A1/1" : "+A1/2", main: i == 11)));
        await using var fake = new CompilationBridge(baseline);
        PlanStep step = await new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("device.update"), Token);
        Assert.Equal("+A1/1", step.Details["pages"]);
        Assert.Equal(61, step.Expected.Length);
        Assert.Equal(61, step.ModifyScope.Locators.Length);
        Assert.Contains("Page +A1/2", PlanDiffPreview.For([step]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compile_DeleteOrphansLongerThanPreview_AreComplete()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(11, 61).SelectMany(i => new[]
        {
            PlanScopeTestData.Function(i, "+A1/1", "+A1-K" + i, true), PlanScopeTestData.Function(i + 1000, "+A1/2", "+A1-K" + i),
        }));
        await using var fake = new CompilationBridge(baseline);
        PlanStep step = await new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("page.delete"), Token);
        Assert.Equal(61, step.Expected.Count(e => e.GetProperty("tag").GetString() == "orphaned"));
        Assert.Equal(123, step.ModifyScope.Locators.Length);
        Assert.Contains("device +A1-K71: main function", PlanDiffPreview.For([step]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compile_MissingFunctionEvidence_IsRefusedInsteadOfNarrowingScope()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        fake.Functions.Functions.Add(new() { Kind = "function", Type = 17, Oid = 99, Name = "+A1-K1", IsMain = false });
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() =>
            new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("device.update"), Token));
        Assert.Equal(ErrorReason.CapabilityMissing, failure.Error.Reason);
        Assert.Contains("snapshot:functions_without_page", failure.Error.Metadata!["missing"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compile_RenumberWholeProjectOverLimit_IsRefused()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([], [.. Enumerable.Range(1, 51).Select(i => "+A1/" + i)]);
        await using var fake = new CompilationBridge(baseline);
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() =>
            new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("page.renumber"), Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains("/modifyScope/pages", failure.Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compile_MissingCompiledDependency_IsInvalidArgumentWithoutBridgeCall()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        PlanArgumentStep input = PlanStepCompilationData.Input("page.update") with { Index = 2, DependsOn = [1] };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(fake.Client).CompileAsync(baseline, input, Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Compile_Cancelled_SendsNoBridgeCall()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("page.update"), cancel.Token));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Compile_BridgePlanningFailure_PropagatesWithoutRetry()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline) { Failure = ErrorReason.EplanBusy };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() =>
            new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("page.update"), Token));
        Assert.Equal(ErrorReason.EplanBusy, failure.Error.Reason);
        Assert.Single(fake.Calls);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("path")]
    public async Task Compile_PlanningProjectDiffersFromBaseline_IsStale(string problem)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        await using var fake = new CompilationBridge(baseline);
        WritePlan plan = fake.Plans["page.update"];
        if (problem == "name")
        { plan.Project = "Other"; }
        else
        { plan.ProjectPath += "-copy"; }
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() =>
            new PlanStepCompiler(fake.Client).CompileAsync(baseline, PlanStepCompilationData.Input("page.update"), Token));
        Assert.Equal(ErrorReason.PlanStale, failure.Error.Reason);
        Assert.Single(fake.Calls);
    }

    private static object ExpectedArgs(string op) => op switch
    {
        "page.create" => new PageCreateArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Language = "en_US", Pages = [new() { Name = "+A1/3", PageType = "Circuit", Description = "Power" }] },
        "page.update" => new PageUpdateArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Language = "en_US", Pages = [new() { Name = "+A1/1", Description = "New" }] },
        "device.update" => new DeviceUpdateArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Tag = "-K1", NewTag = "-K9" },
        "macro.insert" => new MacroInsertArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Page = "+A1/1", Macro = "Motor.ema", X = 40.5, Y = 280.25, Variant = 0, Numbering = "keep" },
        "page.delete" => new PageDeleteArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Pages = ["+A1/1"] },
        "page.renumber" => new PageRenumberArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Pages = ["+A1/1"], Start = 10, Step = 2, KeepText = true, Subpages = "consecutive" },
        "device.renumber" => new DeviceRenumberArgs { Project = PlanSnapshotHashes.ProjectKey(PlanFunctionsBridge.Project), Pages = ["+A1/1"], OnlyUnnumbered = false, Scheme = "IEC", Start = 10, Step = 2 },
        _ => throw new ArgumentException("Unknown test operation.", nameof(op)),
    };

    /// <summary>Both IPC serializers and the real Host client; no real Bridge discovery or EPLAN access.</summary>
    private sealed class CompilationBridge : IAsyncDisposable
    {
        public BridgeClient Client { get; }
        public List<(string Op, JsonNode Args)> Calls { get; } = [];
        public Dictionary<string, WritePlan> Plans { get; } = new(StringComparer.Ordinal);
        public PlanFunctionsData Functions { get; }
        public string? Failure { get; init; }

        public CompilationBridge(PlanBaseline baseline)
        {
            foreach (string op in new[] { "page.create", "page.update", "device.update", "macro.insert", "page.delete", "page.renumber", "device.renumber" })
            { Plans.Add(op, PlanStepCompilationData.Plan(op)); }
            Functions = new() { Project = "Line1", ProjectPath = PlanFunctionsBridge.Project, Complete = true, Limit = 10_000, Functions = [.. PlanScopeTestData.Functions(baseline)] };
            var endpoint = new BridgeEndpoint("EplanMcpTest.PlanCompilation", 123);
            var discovery = new FakeDiscovery();
            discovery.Endpoints.Add(endpoint);
            var connector = new FakeConnector(e => new FakeChannel(e, (_, envelope, _) =>
            {
                string op = envelope!["operation"]!.GetValue<string>();
                Calls.Add((op, envelope["args"]!.DeepClone()));
                if (Failure is not null)
                { throw FakeChannel.BusinessError(Failure); }
                object data;
                if (op == OperationNames.PlanFunctions)
                {
                    PlanFunctionsArgs args = envelope["args"]!.Deserialize<PlanFunctionsArgs>(HostJson.Options)!;
                    data = new PlanFunctionsData
                    {
                        Project = Functions.Project,
                        ProjectPath = Functions.ProjectPath,
                        Complete = Functions.Complete,
                        Limit = Functions.Limit,
                        Functions = [.. Functions.Functions.Where(f => args.Names.Contains(f.Name, StringComparer.Ordinal) || args.Pages?.Contains(f.Page!, StringComparer.Ordinal) == true)]
                    };
                }
                else
                { data = Plans[op[..^5]]; }
                return Task.FromResult(JsonNode.Parse(new JObject { ["data"] = JToken.FromObject(data) }.ToString()));
            })
            { Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId, operations: [.. Plans.Keys.Select(op => op + ".plan"), OperationNames.PlanFunctions])) });
            Client = new(discovery, connector, NullLogger<BridgeClient>.Instance);
        }

        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
