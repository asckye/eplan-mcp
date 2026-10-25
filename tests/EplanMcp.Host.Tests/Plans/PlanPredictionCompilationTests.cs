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
public sealed class PlanPredictionCompilationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("macro.insert")]
    [InlineData("page.update")]
    public async Task Compile_ReferenceToCreatedPage_UsesOnlyReadPlansAndFullFakeInspection(string op)
    {
        object reference = new { from_step = 1, page = PlanPredictionTestData.Page };
        object args = op == "macro.insert" ? new { page = reference, macro = "Motor.ema", x = 40.5, y = 280.25 }
            : new { pages = new[] { new { name = reference, description = "New" } } };
        PlanArgumentCheck request = PlanArgumentsTests.Check(
            PlanArgumentsTests.Step("page.create", new { pages = new[] { new { name = PlanPredictionTestData.Page, description = "Power" } } }),
            PlanArgumentsTests.Step(op, args));
        Assert.Empty(request.Violations);
        await using var bridge = new PredictionBridge();
        var compiler = new PlanStepCompiler(bridge.Client);
        PlanStep create = await compiler.CompileAsync(bridge.Baseline, request.Steps[0], [], Token);
        PlanStep predicted = await compiler.CompileAsync(bridge.Baseline, request.Steps[1], [create], Token);
        Assert.True(predicted.Prediction!.Value.GetProperty("prediction").GetBoolean());
        Assert.Equal([1], predicted.DependsOn);
        Assert.True(Assert.Single(predicted.PlannedChanges).Prediction);
        Assert.Equal(op == "macro.insert" ? new[] { OperationNames.PageCreatePlan, OperationNames.PlanMacroInspect }
            : [OperationNames.PageCreatePlan], bridge.Calls.Select(c => c.Op));
        if (op == "macro.insert")
        {
            Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(new PlanMacroInspectArgs { Macro = "Motor.ema" }, HostJson.Options), bridge.Calls[1].Args));
            Assert.Equal(4, Assert.Single(predicted.Expected).GetProperty("value").GetProperty("functions").GetArrayLength());
        }
        string stored = PlanCanonical.Serialize(PlanTestData.Definition() with { Steps = [create, predicted] });
        PlanDefinition loaded = PlanJson.Read<PlanDefinition>(stored);
        Assert.Equal(PlanCanonical.Hash(loaded), PlanCanonical.Hash(PlanTestData.Definition() with { Steps = [create, predicted] }));
        Assert.True(Assert.Single(loaded.Steps[1].PlannedChanges).Prediction);
        Assert.DoesNotContain(bridge.Calls, c => c.Op.EndsWith(".execute", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ErrorReason.CapabilityMissing, "macro:structure_ownership")]
    [InlineData(ErrorReason.CapabilityMissing, "macro:complete_variant_0")]
    [InlineData(ErrorReason.InvalidArgument, "MultiLine variant 0 is required")]
    public async Task Compile_InspectRefusal_PropagatesReasonAndMetadataWithoutRetry(string reason, string detail)
    {
        await using var bridge = new PredictionBridge
        {
            Failure = new() { Reason = reason, Message = "Choose a supported macro.", Metadata = new() { ["missing"] = JsonSerializer.Serialize(new[] { detail }) } },
        };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, PlanPredictionTestData.Input(), [PlanPredictionTestData.Creation()], Token));
        Assert.Equal(reason, failure.Error.Reason);
        Assert.Contains(detail, failure.Error.Metadata!["missing"], StringComparison.Ordinal);
        Assert.Single(bridge.Calls);
        Assert.Null(failure.Error.Execution);
    }

    [Theory]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public async Task Compile_CreatedPageWithoutRegisteredPredictor_IsInvalidArgument(string op)
    {
        PlanArgumentStep input = PlanPredictionTestData.Input(op, op == "page.delete" ? """{"pages":["+A1/3"]}"""
            : """{"pages":["+A1/3"],"start":10}""");
        await using var bridge = new PredictionBridge();
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, input, [PlanPredictionTestData.Creation()], Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains("no predictor", failure.Error.Message, StringComparison.Ordinal);
        Assert.Contains("/steps/1/args", failure.Error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Empty(bridge.Calls);
    }

    [Theory]
    [InlineData("occupied-page")]
    [InlineData("no-dependency")]
    [InlineData("wrong-type")]
    [InlineData("numbering")]
    [InlineData("variant")]
    [InlineData("overlap-after-create")]
    [InlineData("renumber-before-create")]
    [InlineData("name-conflict")]
    [InlineData("part-conflict")]
    public async Task Compile_UnprovableMacroDependency_IsRefused(string fault)
    {
        await using var bridge = new PredictionBridge();
        PlanStep create = PlanPredictionTestData.Creation();
        PlanArgumentStep input = PlanPredictionTestData.Input();
        List<PlanStep> previous = [create];
        switch (fault)
        {
            case "occupied-page":
                previous.Add(await PlanPredictionTestData.PredictAsync());
                input = input with { Index = 3, DependsOn = [1, 2] };
                break;
            case "overlap-after-create":
                PlanStep update = await PlanPredictionTestData.PredictAsync(input: PlanPredictionTestData.Input("page.update"));
                previous.Add(update);
                input = input with { Index = 3, DependsOn = [1, 2] };
                break;
            case "no-dependency":
                input = input with { DependsOn = [] };
                break;
            case "wrong-type":
                previous[0] = PlanPredictionTestData.Creation("Graphics");
                break;
            case "numbering":
            case "variant":
                JsonNode args = JsonNode.Parse(input.Args.GetRawText())!;
                args[fault] = fault == "numbering" ? JsonValue.Create("number") : JsonValue.Create(1);
                input = input with { Args = JsonSerializer.SerializeToElement(args) };
                break;
            case "renumber-before-create":
                previous.Add(create with { Index = 2, Op = "page.renumber", ModifyScope = PlanScopeRules.Pages(["+A10/1"]) });
                input = input with { Index = 3 };
                break;
            case "name-conflict":
                bridge.Baseline = bridge.Baseline with { Names = [.. bridge.Baseline.Names, new("function", "+A1-K43")] };
                break;
            case "part-conflict":
                bridge.Baseline = bridge.Baseline with { Names = [.. bridge.Baseline.Names, new("function", "+A10-X8:1")] };
                break;
        }
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, input, previous, Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.All(bridge.Calls, c => Assert.Equal(OperationNames.PlanMacroInspect, c.Op));
    }

    [Fact]
    public async Task Compile_DisjointExplicitDependency_UsesTheRealPageUpdatePlan()
    {
        await using var bridge = new PredictionBridge();
        PlanArgumentStep input = PlanStepCompilationData.Input("page.update") with { Index = 2, DependsOn = [1] };
        PlanStep step = await new PlanStepCompiler(bridge.Client).CompileAsync(bridge.Baseline, input, [PlanPredictionTestData.Creation()], Token);
        Assert.Null(step.Prediction);
        Assert.Equal([1], step.DependsOn);
        Assert.Equal(OperationNames.PageUpdatePlan, Assert.Single(bridge.Calls).Op);
    }

    [Fact]
    public async Task Compile_UnpredictedOverlapWithDependency_IsRefusedAfterRealPlanning()
    {
        await using var bridge = new PredictionBridge();
        PlanArgumentStep input = PlanStepCompilationData.Input("page.update") with { Index = 2, DependsOn = [1] };
        PlanStep earlier = PlanPredictionTestData.Creation() with { Op = "page.update", ModifyScope = PlanScopeRules.Pages(["+A1/1"]) };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, input, [earlier], Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains("no predictor", failure.Error.Message, StringComparison.Ordinal);
        Assert.Single(bridge.Calls);
    }

    [Fact]
    public async Task Compile_NoOpPredictionAfterAnotherUpdate_IsRefusedInsteadOfAssumingOriginalValues()
    {
        await using var bridge = new PredictionBridge();
        PlanStep creation = PlanPredictionTestData.Creation();
        PlanStep update = await PlanPredictionTestData.PredictAsync(input: PlanPredictionTestData.Input("page.update"));
        PlanArgumentStep input = PlanPredictionTestData.Input("page.update", """{"pages":[{"name":"+A1/3","description":"Power"}]}""")
            with
        { Index = 3, DependsOn = [1, 2] };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, input, [creation, update], Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task Compile_MacrosOnDifferentCreatedPages_RefusesUnpredictedChangesInTheGlobalNameDomain()
    {
        await using var bridge = new PredictionBridge();
        PlanStep creation = PlanPredictionTestData.Creation() with
        {
            Args = PlanTestData.Json("""{"pages":[{"name":"+A1/3","page_type":"Circuit","description":"Power"},{"name":"+A1/4","page_type":"Circuit","description":null}],"language":"en_US"}"""),
            ModifyScope = PlanScopeRules.Pages(["+A1/3", "+A1/4"]),
        };
        var compiler = new PlanStepCompiler(bridge.Client);
        PlanStep first = await compiler.CompileAsync(bridge.Baseline, PlanPredictionTestData.Input(), [creation], Token);
        Assert.Contains("+A1/4", first.ReadScope.Pages);
        PlanArgumentStep second = PlanPredictionTestData.Input(args: """{"page":"+A1/4","macro":"Motor.ema","x":40.5,"y":280.25}""")
            with
        { Index = 3, DependsOn = [1] };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => compiler
            .CompileAsync(bridge.Baseline, second, [creation, first], Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Contains("macro.insert -> macro.insert", failure.Error.Message, StringComparison.Ordinal);
        Assert.All(bridge.Calls, c => Assert.Equal(OperationNames.PlanMacroInspect, c.Op));
    }

    [Fact]
    public async Task Compile_MissingGlobalPageEvidence_IsCapabilityMissing()
    {
        await using var bridge = new PredictionBridge();
        bridge.Baseline = bridge.Baseline with { Names = [.. bridge.Baseline.Names, new("page", "+A10/2")] };
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, PlanPredictionTestData.Input(), [PlanPredictionTestData.Creation()], Token));
        Assert.Equal(ErrorReason.CapabilityMissing, failure.Error.Reason);
        Assert.Contains("snapshot:project_pages", failure.Error.Metadata!["missing"], StringComparison.Ordinal);
        Assert.Empty(bridge.Calls);
    }

    [Theory]
    [InlineData("sha")]
    [InlineData("objects")]
    [InlineData("functions")]
    public async Task Compile_InvalidInspectBounds_AreRefused(string fault)
    {
        await using var bridge = new PredictionBridge();
        switch (fault)
        {
            case "sha":
                bridge.Inspection.Sha256 = "not-a-digest";
                break;
            case "objects":
                bridge.Inspection.ObjectCount = 1;
                break;
            case "functions":
                bridge.Inspection.Functions = [.. Enumerable.Repeat(bridge.Inspection.Functions[0], 10_001)];
                bridge.Inspection.ObjectCount = 10_001;
                break;
        }
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, PlanPredictionTestData.Input(), [PlanPredictionTestData.Creation()], Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Single(bridge.Calls);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(40, -1)]
    public async Task Compile_NegativeInsertionPosition_IsRefusedBeforeInspection(double x, double y)
    {
        await using var bridge = new PredictionBridge();
        PlanArgumentStep input = PlanPredictionTestData.Input(args: JsonSerializer.Serialize(new
        {
            page = PlanPredictionTestData.Page,
            macro = "Motor.ema",
            x,
            y,
        }));
        BridgeCallException failure = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanStepCompiler(bridge.Client)
            .CompileAsync(bridge.Baseline, input, [PlanPredictionTestData.Creation()], Token));
        Assert.Equal(ErrorReason.InvalidArgument, failure.Error.Reason);
        Assert.Empty(bridge.Calls);
    }

    private sealed class PredictionBridge : IAsyncDisposable
    {
        public BridgeClient Client { get; }
        public PlanBaseline Baseline { get; set; } = PlanScopeTestData.Baseline([]);
        public BridgeError? Failure { get; init; }
        public PlanMacroInspectData Inspection { get; } = PlanPredictionTestData.Macro();
        public List<(string Op, JsonNode Args)> Calls { get; } = [];

        public PredictionBridge()
        {
            var endpoint = new BridgeEndpoint("EplanMcpTest.PlanPrediction", 123);
            var discovery = new FakeDiscovery();
            discovery.Endpoints.Add(endpoint);
            var connector = new FakeConnector(e => new FakeChannel(e, (_, envelope, _) =>
            {
                string op = envelope!["operation"]!.GetValue<string>();
                Calls.Add((op, envelope["args"]!.DeepClone()));
                if (Failure is not null)
                {
                    throw new BridgeRpcException(BridgeProtocol.BusinessErrorCode, Failure.Message, JsonSerializer.SerializeToNode(Failure, HostJson.Options));
                }
                object data = op == OperationNames.PlanMacroInspect ? Inspection
                    : PlanStepCompilationData.Plan(op[..^5]);
                return Task.FromResult(JsonNode.Parse(new JObject { ["data"] = JToken.FromObject(data) }.ToString()));
            })
            {
                Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(e.ProcessId,
                    operations: [OperationNames.PageCreatePlan, OperationNames.PageUpdatePlan, OperationNames.PlanMacroInspect])),
            });
            Client = new(discovery, connector, NullLogger<BridgeClient>.Instance);
        }

        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }
}
