using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tests.Tools;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Medium")]
public sealed class UnverifiedWriteTests : IDisposable
{
    private const string OutsideScratchMessage = "API trials are allowed only in scratch projects.";
    private readonly WriteToolHarness _harness = new();
    private readonly WritePlan _plan;

    public UnverifiedWriteTests()
    {
        _plan = _harness.Plan([new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" }]);
        _harness.Bridge.Handlers[OperationNames.ApiTryPlan] = _ => _plan;
        _harness.Bridge.Handlers[OperationNames.ApiTryExecute] = _ => new JsonObject { ["created"] = 1 };
    }

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OutsideScratch_PreviewAndExecutionAreDeniedBeforeApproval(bool dryRun)
    {
        _plan.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        BridgeError error = await Refused(() => Run(dialog, dryRun: dryRun));

        Assert.Equal((ErrorReason.PolicyDenied, OutsideScratchMessage), (error.Reason, error.Message));
        Assert.Empty(dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
        Assert.DoesNotContain("execution_started", AuditKinds());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task ScratchWrite_CommitsOnceAndAsksForApprovalOnlyWhenUnverified(bool unverified, int questions)
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog, unverified: unverified);

        Assert.Equal(questions, dialog.Questions.Count);
        Assert.Equal(ExecutionState.Committed, outcome.Execution);
        Assert.Single(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ScratchWrite_WhenApprovalIsDenied_DoesNotExecute()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);

        BridgeError error = await Refused(() => Run(dialog));

        Assert.Equal(ErrorReason.ApprovalDenied, error.Reason);
        Assert.Single(dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ScratchWrite_WithoutAChannel_ReportsApprovalUnavailable()
    {
        BridgeError error = await Refused(() => Run(null));

        Assert.Equal((ErrorReason.ApprovalUnavailable, ApprovalService.NoChannel), (error.Reason, error.Message));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ScratchMasterData_WhenPolicyDeniesWrites_IsDeniedWithoutApproval()
    {
        SetPartsDatabase(_harness.Locations.DefaultScratch);
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        BridgeError error = await Refused(() => Run(dialog, masterDataWrites: MasterDataWrites.Deny));

        Assert.Equal((ErrorReason.PolicyDenied, PolicyEngine.MasterDataDenied), (error.Reason, error.Message));
        Assert.Empty(dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartsDatabaseOutsideScratch_PreviewAndExecutionAreDenied(bool dryRun)
    {
        SetPartsDatabase(_harness.Locations.Root);
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        BridgeError error = await Refused(() => Run(dialog, dryRun: dryRun));

        Assert.Equal((ErrorReason.PolicyDenied, WritePipeline.MasterDataCopyDenied), (error.Reason, error.Message));
        Assert.Empty(dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task PartsDatabaseInsideScratch_ReachesApproval()
    {
        SetPartsDatabase(_harness.Locations.DefaultScratch);
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Denied);

        BridgeError error = await Refused(() => Run(dialog));

        Assert.Equal(ErrorReason.ApprovalDenied, error.Reason);
        Assert.Equal(_plan.MasterData!.Path, Assert.Single(dialog.Questions).Plan.MasterData!.Path);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ScratchPreview_ReturnsThePlanWithoutApprovalOrExecution()
    {
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted);

        WriteOutcome<JsonObject> outcome = await Run(dialog, dryRun: true);

        Assert.Equal(ExecutionState.NotStarted, outcome.Execution);
        Assert.Same(_plan, outcome.Plan);
        Assert.Empty(dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ScratchDataFolder_RedirectedOutsideDuringApproval_IsDeniedBeforeExecution()
    {
        Directory.CreateDirectory(_harness.Locations.DefaultScratch);
        string outside = Directory.CreateDirectory(Path.Combine(_harness.Locations.Root, "Projects", "Line1.edb")).FullName;
        string link = Path.Combine(_harness.Locations.Root, "redirect");
        string dataFolder = _plan.ProjectPath + ".edb";
        await PlanStoreFixture.Junction(link, outside);
        var dialog = new FakeApprovalChannel("eplan_dialog", ApprovalDecision.Granted)
        {
            Wait = () => Directory.Move(link, dataFolder),
        };

        try
        {
            BridgeError error = await Refused(() => Run(dialog));

            Assert.Equal((ErrorReason.PolicyDenied, OutsideScratchMessage), (error.Reason, error.Message));
            Assert.Single(dialog.Questions);
            Assert.Empty(_harness.Bridge.Writes);
        }
        finally
        {
            Directory.Delete(Directory.Exists(dataFolder) ? dataFolder : link);
        }
    }

    private void SetPartsDatabase(string root)
    {
        string folder = Directory.CreateDirectory(Path.Combine(root, "parts")).FullName;
        string path = Path.Combine(folder, "Copy.mdb");
        File.WriteAllText(path, string.Empty);
        _plan.MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = path, Path = path };
    }

    private Task<WriteOutcome<JsonObject>> Run(FakeApprovalChannel? channel, bool dryRun = false,
        bool unverified = true, MasterDataWrites masterDataWrites = MasterDataWrites.Approve)
    {
        HostConfig config = HostConfig.Defaults(_harness.Locations) with { MasterDataWrites = masterDataWrites };
        var pipeline = TestWritePipeline.Create(_harness.Bridge, _harness.Locations, config,
            new ManualTime(DateTimeOffset.UnixEpoch), channel is null ? [] : [channel]);
        var request = new WriteRequest<object, object>(ApiTryTools.Tool, ToolTier.Write, OperationNames.ApiTryPlan,
            OperationNames.ApiTryExecute, new { pages = 1 }, plan => new { plan.Fingerprint }, dryRun, TimeSpan.FromSeconds(60))
        {
            Unverified = unverified,
        };
        return pipeline.RunAsync<object, object, JsonObject>(request, TestContext.Current.CancellationToken);
    }

    private static async Task<BridgeError> Refused(Func<Task> run)
    {
        BridgeError error = (await Assert.ThrowsAsync<BridgeCallException>(run)).Error;
        Assert.Equal(ExecutionState.NotStarted, error.Execution);
        return error;
    }

    private IEnumerable<string> AuditKinds() => Directory.Exists(_harness.Locations.Audit)
        ? Directory.EnumerateFiles(_harness.Locations.Audit, AuditLog.FilePattern)
            .SelectMany(File.ReadAllLines).Select(line => JsonNode.Parse(line)!["kind"]!.GetValue<string>())
        : [];
}
