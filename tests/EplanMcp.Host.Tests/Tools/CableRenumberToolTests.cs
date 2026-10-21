using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_cable_renumber through the real write pipeline against a fake Bridge (docs/tools/eplan_cable_renumber.md,
/// sections 3, 4, 6 and 7), with what spike S21 found EPLAN does to the sample project's cables (api-notes A24).
/// </summary>
[Trait("Size", "Medium")]
public sealed class CableRenumberToolTests : IDisposable
{
    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;
    private int _cables = 78;

    public CableRenumberToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.CableRenumberPlan] = _ =>
        {
            WritePlan plan = _harness.Plan(_cables == 0 ? [] : [new PlannedChange { Action = "update", Kind = "cables", Name = "Line1", Property = "numbering", OldValue = $"all cable tags in the project ({_cables} cables), scheme: Standard, start 1, step 1" }]);
            plan.Details = new Dictionary<string, string>
            {
                ["cables"] = _cables.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["cableCount"] = string.Empty,
                ["keepExisting"] = "false",
                ["scheme"] = "Standard",
                ["start"] = "1",
                ["step"] = "1",
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.CableRenumberExecute] = args =>
        {
            File.WriteAllText(((CableRenumberArgs)args!).Backup!.Path, "copy");
            return new CableRenumberData
            {
                Project = "Line1",
                Changed = [Renamed("+A2-WD4", "+A2-WD5"), Renamed("+A2-WD5", "+A2-WD4"), Renamed("+B2.Y1-WD2", "+B2.Y1-WD1"), Renamed("+B2.Y1-WD1", "+B2.Y1-WD2")],
                ChangedCount = 4,
            };
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ShowsTheScopeOnly()
    {
        CallToolResult result = await Renumber(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        JsonElement scope = data.GetProperty("scope");
        Assert.Equal((JsonValueKind.Null, false, "Standard", 1, 1, 78), (scope.GetProperty("cableCount").ValueKind, scope.GetProperty("keepExisting").GetBoolean(), scope.GetProperty("scheme").GetString(), scope.GetProperty("start").GetInt32(), scope.GetProperty("step").GetInt32(), scope.GetProperty("cables").GetInt32()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("changed").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("Dry run: would renumber 78 cables in the project; the new tags are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenEplanNumbersAfterABackup()
    {
        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(["renumber all cable tags in the project (78 cables), scheme: Standard, start 1, step 1"], Assert.Single(_dialog.Questions).Changes);
        var sent = (CableRenumberArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", null, "Standard", false, 150), (sent.Fingerprint, sent.Cables, sent.Scheme, sent.KeepExisting, sent.Backup!.MaxSeconds));
        Assert.Equal(("committed", 4, "AI: eplan_cable_renumber", sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("changedCount").GetInt32(), data.GetProperty("undo").GetString(), data.GetProperty("backup").GetString()));
        Assert.Equal(("+A2-WD4", "+A2-WD5"), (data.GetProperty("changed")[0].GetProperty("from").GetString(), data.GetProperty("changed")[0].GetProperty("to").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("hint").ValueKind);
        Assert.Equal("Renumbered 4 cables. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task NothingToNumber_IsNotStartedAndAsksNobody()
    {
        _cables = 0;

        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", "There is no cable in scope to number."), (data.GetProperty("execution").GetString(), data.GetProperty("hint").GetString()));
        Assert.Equal("Nothing to number. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task EplanRenamingNoCable_IsNotStartedWithTheBackupAndNothingToUndo()
    {
        _harness.Bridge.Handlers[OperationNames.CableRenumberExecute] = args =>
        {
            File.WriteAllText(((CableRenumberArgs)args!).Backup!.Path, "copy");
            return new CableRenumberData { Project = "Line1" };
        };

        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.String), (data.GetProperty("execution").GetString(), data.GetProperty("undo").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("EPLAN renamed no cable, so nothing was changed. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task ADeviceThatIsNoCable_IsNotStartedWithItsCategory()
    {
        _harness.Bridge.Handlers[OperationNames.CableRenumberPlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "Some tags name devices that are not cables: -FC1 (CircuitBreaker).",
            Metadata = new Dictionary<string, string> { ["notCables"] = "-FC1 (CircuitBreaker)" },
        });

        JsonElement error = WriteToolHarness.Error(await CableDestructiveTools.CableRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", "Standard", ["-FC1"], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted, "-FC1 (CircuitBreaker)"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("notCables").GetString()));
        Assert.Empty(_dialog.Questions);
    }

    [Theory]
    [InlineData("", 1, 1, "'scheme' is required: a cable numbering scheme such as Standard.")]
    [InlineData("Standard", 0, 1, "'cables' must hold 1 to 200 cable tags; omit it for every cable of the project.")]
    [InlineData("Standard", 1, 0, "'start' and 'step' must be at least 1.")]
    public async Task ArgumentProblems_AreRefusedBeforeEplanIsAsked(string scheme, int cables, int step, string violation)
    {
        string[] tags = [.. Enumerable.Repeat("+A2-WD4", cables)];

        JsonElement error = WriteToolHarness.Error(await CableDestructiveTools.CableRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", scheme, tags, step: step, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(violation, error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private static RenamedObject Renamed(string from, string to) => new() { From = from, To = to };

    private Task<CallToolResult> Renumber(bool dryRun = false) =>
        CableDestructiveTools.CableRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", "Standard", dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
