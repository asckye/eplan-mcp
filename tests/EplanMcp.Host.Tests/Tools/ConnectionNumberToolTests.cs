using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_connection_number through the real write pipeline against a fake Bridge (docs/tools/eplan_connection_number.md,
/// sections 3, 4, 6 and 7), with what spike S21 found EPLAN does to the sample project (api-notes A24: 605 wire numbers).
/// </summary>
[Trait("Size", "Medium")]
public sealed class ConnectionNumberToolTests : IDisposable
{
    private const string Line = "renumber wire numbers in the project (2060 connections, 116 numbered), scheme: Standard, overwrite: except manual";

    private readonly FakeApprovalChannel _dialog = new("eplan_dialog", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;
    private int _connections = 2060;

    public ConnectionNumberToolTests()
    {
        _harness = new WriteToolHarness(_dialog);
        _harness.Bridge.Handlers[OperationNames.ConnectionNumberPlan] = args =>
        {
            var given = (ConnectionNumberArgs)args!;
            WritePlan plan = _harness.Plan(_connections == 0 ? [] : [new PlannedChange { Action = "update", Kind = "connections", Name = "Line1", Property = "numbering", OldValue = Line["renumber ".Length..] }]);
            plan.Details = new Dictionary<string, string>
            {
                ["connections"] = _connections.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["designatedBefore"] = _connections == 0 ? "0" : "116",
                ["scheme"] = given.Scheme!,
                ["overwrite"] = given.Overwrite,
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ConnectionNumberExecute] = args =>
        {
            File.WriteAllText(((ConnectionNumberArgs)args!).Backup!.Path, "copy");
            return new ConnectionNumberData
            {
                Project = "Line1",
                Changed = [.. Enumerable.Range(0, 200).Select(i => new NumberedConnection { From = i == 0 ? "*" : null, To = "4" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), Page = i == 1 ? null : "=F+A2&EFS1/2" })],
                ChangedCount = 605,
                DesignatedAfter = 659,
            };
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ShowsTheScopeOnly()
    {
        CallToolResult result = await Number(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        JsonElement scope = data.GetProperty("scope");
        Assert.Equal(("Standard", "except_manual", 2060, 116), (scope.GetProperty("scheme").GetString(), scope.GetProperty("overwrite").GetString(), scope.GetProperty("connections").GetInt32(), scope.GetProperty("designatedBefore").GetInt32()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("changed").ValueKind, data.GetProperty("designatedAfter").ValueKind, data.GetProperty("backup").ValueKind));
        Assert.Equal("Dry run: would number the 2060 connections of the project (116 with a wire number); the new wire numbers are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task InScratch_TheEngineerIsAsked_ThenEplanNumbersAfterABackup_AndTheListIsCut()
    {
        CallToolResult result = await Number(overwrite: "all");

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal([Line], Assert.Single(_dialog.Questions).Changes);
        var sent = (ConnectionNumberArgs)_harness.Bridge.LastArgs!;
        Assert.Equal(("f1", "Standard", "all", 150), (sent.Fingerprint, sent.Scheme, sent.Overwrite, sent.Backup!.MaxSeconds));
        Assert.Equal(("committed", 605, 659, "AI: eplan_connection_number", sent.Backup.Path), (data.GetProperty("execution").GetString(), data.GetProperty("changedCount").GetInt32(), data.GetProperty("designatedAfter").GetInt32(), data.GetProperty("undo").GetString(), data.GetProperty("backup").GetString()));
        JsonElement[] changed = [.. data.GetProperty("changed").EnumerateArray()];
        Assert.Equal((200, "*", "400", JsonValueKind.Null, JsonValueKind.Null), (changed.Length, changed[0].GetProperty("from").GetString(), changed[0].GetProperty("to").GetString(), changed[1].GetProperty("from").ValueKind, changed[1].GetProperty("page").ValueKind));
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Equal("605 connections changed; the first 200 are listed.", data.GetProperty("hint").GetString());
        Assert.Equal("Numbered 605 connections. A backup was made first (see 'backup'); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task AProjectWithoutConnections_IsNotStartedAndAsksNobody()
    {
        _connections = 0;

        CallToolResult result = await Number();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal("not_started", data.GetProperty("execution").GetString());
        Assert.StartsWith("The project has no connections to number", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Nothing to number. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_dialog.Questions);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task EplanChangingNoWireNumber_IsNotStartedWithTheBackupAndNothingToUndo()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionNumberExecute] = args =>
        {
            File.WriteAllText(((ConnectionNumberArgs)args!).Backup!.Path, "copy");
            return new ConnectionNumberData { Project = "Line1", DesignatedAfter = 116 };
        };

        CallToolResult result = await Number();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", JsonValueKind.Null, JsonValueKind.String, "EPLAN changed no wire number."), (data.GetProperty("execution").GetString(), data.GetProperty("undo").ValueKind, data.GetProperty("backup").ValueKind, data.GetProperty("hint").GetString()));
        Assert.Equal("EPLAN changed no wire number, so nothing was changed. A backup was made first (see 'backup').", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task AnUnknownScheme_IsNotStartedWithItsCandidates()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionNumberPlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.InvalidArgument,
            Message = "EPLAN has no wire numbering scheme 'Potential', so nothing was numbered.",
            Metadata = new Dictionary<string, string> { ["scheme"] = "Potential", ["candidates"] = "Standard\nPotenzialorientiert" },
        });

        JsonElement error = WriteToolHarness.Error(await ConnectionDestructiveTools.ConnectionNumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", "Potential", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted, "Standard\nPotenzialorientiert"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("candidates").GetString()));
        Assert.Empty(_dialog.Questions);
    }

    [Fact]
    public async Task AMissingScheme_IsRefusedBeforeEplanIsAsked()
    {
        JsonElement error = WriteToolHarness.Error(await ConnectionDestructiveTools.ConnectionNumberAsync(_harness.Pipeline, new RecordedProgress(), " ", " ", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\n'scheme' is required: a wire numbering scheme such as Standard.",
            error.GetProperty("metadata").GetProperty("violations").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> Number(bool dryRun = false, string overwrite = "except_manual") =>
        ConnectionDestructiveTools.ConnectionNumberAsync(_harness.Pipeline, new RecordedProgress(), "Line1", "Standard", overwrite, dryRun, TestContext.Current.CancellationToken);
}
