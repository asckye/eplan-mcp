using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_check_run through the real write pipeline against a fake Bridge (docs/tools/eplan_check_run.md, sections 3,
/// 4, 6 and 7). The sample project has 321 pages and no check messages; a check finds 6 warnings and 14 hints (S6, S11).
/// </summary>
[Trait("Size", "Medium")]
public sealed class CheckRunToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new();

    public CheckRunToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.CheckRunPlan] = args =>
        {
            var check = (CheckRunArgs)args!;
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "check messages", OldValue = "0" }]);
            plan.Details = new Dictionary<string, string> { ["pageCount"] = check.Pages?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "321" };
            if (check.Scheme is { } scheme)
            {
                plan.Details["scheme"] = scheme;
            }

            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.CheckRunExecute] = _ => Data(warnings: 6, hints: 14);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_GivesTheScopeAndTheMessagesNow_AndNeverExecutes()
    {
        CallToolResult result = await Run(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, 321, 0), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("pageCount").GetInt32(), data.GetProperty("before").GetInt32()));
        Assert.All(["scheme", "counts", "messages", "total", "undo"], name => Assert.Equal(JsonValueKind.Null, data.GetProperty(name).ValueKind));
        Assert.Contains("known only after running", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Dry run: would check 321 pages and replace the 0 existing check messages. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ACommit_CountsAndListsTheMessagesAsCheckListDoes()
    {
        CallToolResult result = await Run(pages: ["=GAA+A1&EFS1/1"], scheme: "Mine");

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_check_run", false, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("truncated").GetBoolean(), data.GetProperty("hint").ValueKind));
        Assert.Equal((0, 6, 14, 20), (data.GetProperty("counts").GetProperty("error").GetInt32(), data.GetProperty("counts").GetProperty("warning").GetInt32(), data.GetProperty("counts").GetProperty("hint").GetInt32(), data.GetProperty("total").GetInt32()));
        JsonElement first = data.GetProperty("messages")[0];
        Assert.Equal((4001, "warning", "Connections", "Terminal", "=GAA+A1-X1:1"), (first.GetProperty("number").GetInt32(), first.GetProperty("severity").GetString(), first.GetProperty("region").GetString(), first.GetProperty("object").GetProperty("kind").GetString(), first.GetProperty("object").GetProperty("name").GetString()));
        Assert.Equal("Check found 6 warnings, 14 hints (0 messages before); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (CheckRunArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.CheckRunExecute).Args!;
        Assert.Equal(["=GAA+A1&EFS1/1"], executed.Pages!);
        Assert.Equal(("Mine", "f1"), (executed.Scheme, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(120), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task MoreThan20Messages_AreTruncatedWithAHintToCheckList()
    {
        _harness.Bridge.Handlers[OperationNames.CheckRunExecute] = _ => Data(errors: 15, warnings: 6, hints: 14);

        CallToolResult result = await Run();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal((true, 20, 35), (data.GetProperty("truncated").GetBoolean(), data.GetProperty("messages").GetArrayLength(), data.GetProperty("total").GetInt32()));
        Assert.Contains("Listed 20 of 35 messages; eplan_check_list", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Check found 15 errors, 6 warnings, 14 hints (0 messages before); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task ACheckThatFindsNothing_SaysSoAndStillCommits()
    {
        _harness.Bridge.Handlers[OperationNames.CheckRunExecute] = _ => Data(before: 3);

        CallToolResult result = await Run();

        Assert.Equal("committed", WriteToolHarness.Structured(result).GetProperty("execution").GetString());
        Assert.Equal("Check found no messages (3 messages before); Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task ABlankScheme_IsNoScheme()
    {
        await Run(scheme: string.Empty);

        Assert.Null(((CheckRunArgs)_harness.Bridge.Calls[0].Args!).Scheme);
    }

    [Fact]
    public async Task ARecoveredCommit_HasNoMessagesAndPointsToCheckList()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Run(scheme: "Mine");

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", true, "Mine", JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean(), data.GetProperty("scheme").GetString(), data.GetProperty("messages").ValueKind));
        Assert.Contains("eplan_check_list", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Ran the check on 321 pages; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task MissingPages_FromThePlan_AreNotStarted()
    {
        _harness.Bridge.Handlers[OperationNames.CheckRunPlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.PageNotFound,
            Message = "No page has these names.",
            Metadata = new Dictionary<string, string> { ["missing"] = "=X/1\n=X/2" },
        });

        JsonElement error = WriteToolHarness.Error(await Run(pages: ["=X/1", "=GAA+A1&EFS1/1", "=X/2"]));

        Assert.Equal(("PAGE_NOT_FOUND", "not_started", "=X/1\n=X/2"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("missing").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.InvalidArgument, ExecutionState.RolledBack)] // a scheme EPLAN does not know
    public async Task ABridgeError_KeepsItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution };

        JsonElement error = WriteToolHarness.Error(await Run(scheme: "Nope"));

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Run());

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(" ", 1, "'project' is required")]
    [InlineData("Line1", 0, "'pages' must hold 1 to 50 full page names; omit it for the whole project.")]
    [InlineData("Line1", 51, "'pages' must hold 1 to 50 full page names; omit it for the whole project.")]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked(string project, int count, string violation)
    {
        string[] pages = [.. Enumerable.Range(1, count).Select(n => "=A/" + n)];

        JsonElement error = WriteToolHarness.Error(await CheckWriteTools.CheckRunAsync(_harness.Pipeline, new RecordedProgress(), project, pages, null, false, TestContext.Current.CancellationToken));

        Assert.Contains(violation, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public void TheApprovalLines_GiveTheScopeAndTheMessagesReplaced()
    {
        var change = new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "check messages", OldValue = "0" };
        var all = new WritePlan { Changes = [change], Details = new Dictionary<string, string> { ["pageCount"] = "321" } };
        var some = new WritePlan { Changes = [change], Details = new Dictionary<string, string> { ["pageCount"] = "2", ["scheme"] = "Mine" } };

        Assert.Equal(["run check on 321 pages (EPLAN's current scheme), replacing the 0 existing check messages"], WritePipeline.ChangeLines(all));
        Assert.Equal(["run check on 2 pages (scheme: Mine), replacing the 0 existing check messages"], WritePipeline.ChangeLines(some));
        Assert.Equal("Run the check on 321 pages (EPLAN's current scheme), replacing the 0 existing check messages", ApprovalTexts.Change(change, zh: false, all.Details));
        Assert.Equal("运行检查：2 页（方案：Mine），替换现有的 0 条检查消息", ApprovalTexts.Change(change, zh: true, some.Details));
    }

    private static CheckRunData Data(int errors = 0, int warnings = 0, int hints = 0, int before = 0)
    {
        List<CheckMessage> messages =
        [
            .. Enumerable.Range(1, warnings).Select(i => Message(4000 + i, CheckMessage.Warning)),
            .. Enumerable.Range(1, hints).Select(i => Message(13000 + i, CheckMessage.Hint)),
            .. Enumerable.Range(1, errors).Select(i => Message(7000 + i, CheckMessage.Error)),
        ];
        return new CheckRunData
        {
            Project = "Line1",
            PageCount = 321,
            Before = before,
            Messages = [.. messages.Take(20)],
            Total = messages.Count,
            Errors = errors,
            Warnings = warnings,
            Hints = hints,
        };
    }

    private static CheckMessage Message(int number, string severity) => new()
    {
        Number = number,
        Severity = severity,
        Region = "Connections",
        Text = "Terminal not connected",
        Target = new CheckObject { Kind = "Terminal", Name = "=GAA+A1-X1:1" },
    };

    private Task<CallToolResult> Run(string[]? pages = null, string? scheme = null, bool dryRun = false) =>
        CheckWriteTools.CheckRunAsync(_harness.Pipeline, new RecordedProgress(), "Line1", pages, scheme, dryRun, TestContext.Current.CancellationToken);
}
