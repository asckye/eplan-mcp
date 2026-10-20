using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_device_renumber through the real write pipeline against a fake Bridge (docs/tools/eplan_device_renumber.md,
/// sections 3, 4, 6 and 7), with what spike S12 found EPLAN does to the macro page (api-notes A18).
/// </summary>
[Trait("Size", "Medium")]
public sealed class DeviceRenumberToolTests : IDisposable
{
    private const string Page = "=GAA+A1&EFS1/60";

    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;
    private string _devices = "=GAA+A1-?QA1\n+A1-?XG1\n+A1-?XG2\n+A1-?WG1";

    public DeviceRenumberToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.DeviceRenumberPlan] = _ =>
        {
            int count = _devices.Length == 0 ? 0 : _devices.Split('\n').Length;
            WritePlan plan = _harness.Plan(count == 0 ? [] : [new PlannedChange { Action = "update", Kind = "devices", Name = "Line1", Property = "device tags", OldValue = $"device tags containing '?' on 1 page ({count} devices), scheme: EPLAN default, start 1, step 1" }]);
            plan.Details = new Dictionary<string, string>
            {
                ["devices"] = _devices,
                ["deviceCount"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["pageCount"] = "1",
                ["onlyUnnumbered"] = "true",
                ["scheme"] = string.Empty,
                ["start"] = "1",
                ["step"] = "1",
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DeviceRenumberExecute] = args =>
        {
            File.WriteAllText(((DeviceRenumberArgs)args!).Backup!.Path, "copy");
            return new DeviceRenumberData
            {
                Project = "Line1",
                Changed = [new RenamedObject { From = "+A1-?XG1", To = "+A1-XG2" }, new RenamedObject { From = "+A1-?XG2", To = "+A1-XG3" }, new RenamedObject { From = "+A1-?WG1", To = "+A1-WG1" }],
                ChangedCount = 3,
                StillUnnumbered = ["=GAA+A1-?QA1"],
                StillUnnumberedCount = 1,
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
        Assert.Equal((1, true, JsonValueKind.Null, 1, 1, 4), (scope.GetProperty("pageCount").GetInt32(), scope.GetProperty("onlyUnnumbered").GetBoolean(), scope.GetProperty("scheme").ValueKind, scope.GetProperty("start").GetInt32(), scope.GetProperty("step").GetInt32(), scope.GetProperty("devices").GetInt32()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("changed").ValueKind, data.GetProperty("stillUnnumbered").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("Dry run: would number the 4 devices with '?' on 1 page; the new tags are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenEplanNumbersAfterABackup()
    {
        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["renumber device tags containing '?' on 1 page (4 devices), scheme: EPLAN default, start 1, step 1"], question.Changes);
        var sent = (DeviceRenumberArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", true, 1, 1, 150), (sent.Fingerprint, sent.OnlyUnnumbered, sent.Start, sent.Step, sent.Backup!.MaxSeconds));
        Assert.Equal(("committed", 3, "AI: eplan_device_renumber", sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("changedCount").GetInt32(), data.GetProperty("undo").GetString(), data.GetProperty("backup").GetString()));
        Assert.Equal(["+A1-WG1", "+A1-XG2", "+A1-XG3"], data.GetProperty("changed").EnumerateArray().Select(c => c.GetProperty("to").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal("=GAA+A1-?QA1", Assert.Single(data.GetProperty("stillUnnumbered").EnumerateArray()).GetString());
        Assert.Equal("1 device tag still contains '?' after numbering; check it in EPLAN.", data.GetProperty("hint").GetString());
        Assert.Equal("Numbered 3 device tags; 1 still contains '?'. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task NothingToNumber_IsNotStartedAndAsksNobody()
    {
        _devices = string.Empty;

        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal("not_started", data.GetProperty("execution").GetString());
        Assert.StartsWith("No device tag in scope contains '?'", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Nothing to number. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task AnUnknownScheme_IsRolledBackWithItsCandidatesAndTheBackup()
    {
        _harness.Bridge.WriteFailure = new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "EPLAN has no numbering scheme 'Default'.",
            Execution = ExecutionState.RolledBack,
            Metadata = new Dictionary<string, string> { ["candidates"] = "Standard\nMedia_code" },
        };

        JsonElement error = WriteToolHarness.Error(await Renumber());

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack, "Standard\nMedia_code"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("candidates").GetString()));
        Assert.StartsWith(_harness.Locations.Backups, error.GetProperty("metadata").GetProperty("backup").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1, "'pages' must hold 1 to 50 full page names; omit it for the whole project.")]
    [InlineData(1, 0, "'start' and 'step' must be at least 1.")]
    public async Task ArgumentProblems_AreRefusedBeforeEplanIsAsked(int pages, int step, string violation)
    {
        string[] names = [.. Enumerable.Repeat(Page, pages)];

        JsonElement error = WriteToolHarness.Error(await DeviceDestructiveTools.DeviceRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", names, step: step, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(violation, error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Renumber(bool dryRun = false) =>
        DeviceDestructiveTools.DeviceRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", [Page], dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
