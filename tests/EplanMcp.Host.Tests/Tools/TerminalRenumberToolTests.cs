using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_terminal_renumber through the real write pipeline against a fake Bridge (docs/tools/eplan_terminal_renumber.md,
/// sections 3, 4, 6 and 7), with what spike S21 found EPLAN does to the strip +A1-XD1 (api-notes A24).
/// </summary>
[Trait("Size", "Medium")]
public sealed class TerminalRenumberToolTests : IDisposable
{
    private const string Strip = "+A1-XD1";

    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;
    private int _terminals = 5;

    public TerminalRenumberToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.TerminalRenumberPlan] = _ =>
        {
            WritePlan plan = _harness.Plan(_terminals == 0 ? [] : [new PlannedChange { Action = "update", Kind = "terminals", Name = "Line1", Property = "numbering", OldValue = $"all terminal designations on 1 strip ({_terminals} terminals), scheme: Numerisch, start 1, step 1" }]);
            plan.Details = new Dictionary<string, string>
            {
                ["terminalCount"] = _terminals.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["stripCount"] = "1",
                ["onlyUnnumbered"] = "false",
                ["scheme"] = "Numerisch",
                ["start"] = "1",
                ["step"] = "1",
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.TerminalRenumberExecute] = args =>
        {
            File.WriteAllText(((TerminalRenumberArgs)args!).Backup!.Path, "copy");
            return new TerminalRenumberData
            {
                Project = "Line1",
                Changed = [Renamed(":2", ":3"), Renamed(":3", ":5"), Renamed(":4", ":2"), Renamed(":5", ":4")],
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
        Assert.Equal((1, false, "Numerisch", 1, 1, 5), (scope.GetProperty("stripCount").GetInt32(), scope.GetProperty("onlyUnnumbered").GetBoolean(), scope.GetProperty("scheme").GetString(), scope.GetProperty("start").GetInt32(), scope.GetProperty("step").GetInt32(), scope.GetProperty("terminals").GetInt32()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("changed").ValueKind, data.GetProperty("stillUnnumbered").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("Dry run: would renumber all 5 terminals on 1 strip; the new names are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenEplanNumbersAfterABackup()
    {
        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        ApprovalQuestion question = Assert.Single(_dialog.Questions);
        Assert.Equal(["renumber all terminal designations on 1 strip (5 terminals), scheme: Numerisch, start 1, step 1"], question.Changes);
        var sent = (TerminalRenumberArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", Strip, "Numerisch", false, 150), (sent.Fingerprint, Assert.Single(sent.Strips!), sent.Scheme, sent.OnlyUnnumbered, sent.Backup!.MaxSeconds));
        Assert.Equal(("committed", 4, "AI: eplan_terminal_renumber", sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("changedCount").GetInt32(), data.GetProperty("undo").GetString(), data.GetProperty("backup").GetString()));
        Assert.Equal(Strip + ":3", data.GetProperty("changed")[0].GetProperty("to").GetString());
        Assert.Empty(data.GetProperty("stillUnnumbered").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("hint").ValueKind);
        Assert.Equal("Numbered 4 terminals. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task NothingToNumber_IsNotStartedAndAsksNobody()
    {
        _terminals = 0;

        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", "There is no terminal in scope to number."), (data.GetProperty("execution").GetString(), data.GetProperty("hint").GetString()));
        Assert.Equal("Nothing to number. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task EplanRenamingNoTerminal_IsNotStartedWithTheBackupAndNothingToUndo()
    {
        _harness.Bridge.Handlers[OperationNames.TerminalRenumberExecute] = args =>
        {
            File.WriteAllText(((TerminalRenumberArgs)args!).Backup!.Path, "copy");
            return new TerminalRenumberData { Project = "Line1", ChangedCount = 0, StillUnnumbered = [Strip + ":?1"], StillUnnumberedCount = 1 };
        };

        CallToolResult result = await Renumber();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.String), (data.GetProperty("execution").GetString(), data.GetProperty("undo").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("EPLAN renamed no terminal. 1 terminal still has '?' in its designation after numbering; check it in EPLAN.", data.GetProperty("hint").GetString());
        Assert.Equal("EPLAN renamed no terminal, so nothing was changed. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task AnUnknownScheme_IsNotStartedWithItsCandidates()
    {
        _harness.Bridge.Handlers[OperationNames.TerminalRenumberPlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "EPLAN has no terminal numbering scheme 'numerisch', so nothing was numbered.",
            Metadata = new Dictionary<string, string> { ["scheme"] = "numerisch", ["candidates"] = "Gerätedefinition\nInitiatoren\nNumerisch" },
        });

        JsonElement error = WriteToolHarness.Error(await TerminalDestructiveTools.TerminalRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", "numerisch", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal("Gerätedefinition\nInitiatoren\nNumerisch", error.GetProperty("metadata").GetProperty("candidates").GetString());
        Assert.Empty(_dialog.Questions);
    }

    [Theory]
    [InlineData(" ", 1, 1, "'scheme' is required: a terminal numbering scheme such as Numerisch.")]
    [InlineData("Numerisch", 0, 1, "'strips' must hold 1 to 50 terminal strip names; omit it for the whole project.")]
    [InlineData("Numerisch", 1, 0, "'start' and 'step' must be at least 1.")]
    public async Task ArgumentProblems_AreRefusedBeforeEplanIsAsked(string scheme, int strips, int step, string violation)
    {
        string[] names = [.. Enumerable.Repeat(Strip, strips)];

        JsonElement error = WriteToolHarness.Error(await TerminalDestructiveTools.TerminalRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", scheme, names, step: step, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(violation, error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private static RenamedObject Renamed(string from, string to) => new() { From = Strip + from, To = Strip + to };

    private Task<CallToolResult> Renumber(bool dryRun = false) =>
        TerminalDestructiveTools.TerminalRenumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", "Numerisch", [Strip], only_unnumbered: false, dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
