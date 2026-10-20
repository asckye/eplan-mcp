using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_connection_generate through the real write pipeline against a fake Bridge
/// (docs/tools/eplan_connection_generate.md, sections 3, 4, 6 and 7). Page /60 holds a macro whose 9 connections EPLAN
/// generates (S10); page /1 has 40 already.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ConnectionGenerateToolTests : IDisposable
{
    private const string NewPage = "=GAA+A1&EFS1/60";
    private const string FullPage = "=GAA+A1&EFS1/1";

    private readonly WriteToolHarness _harness = new();

    public ConnectionGenerateToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionGeneratePlan] = args => _harness.Plan(
            ((ConnectionGenerateArgs)args!).Pages.Select(page => new PlannedChange { Action = "update", Kind = "page", Name = page, Property = "connections", OldValue = page == FullPage ? "40" : "0" }));
        _harness.Bridge.Handlers[OperationNames.ConnectionGenerateExecute] = _ => Data(
            new ConnectionGeneratePage { Page = NewPage, After = 9, AddedCount = 9, Added = [.. Enumerable.Range(1, 9).Select(i => Wire("+A1-XG9:?" + i, null, "=GAA+A1-?QA1", "1" + i))] },
            new ConnectionGeneratePage { Page = FullPage, Before = 40, After = 40 });
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_CountsTheConnectionsNowAndNeverExecutes()
    {
        CallToolResult result = await Generate([NewPage], dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("undo").ValueKind));
        JsonElement page = Assert.Single(data.GetProperty("pages").EnumerateArray());
        Assert.Equal((NewPage, 0, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (page.GetProperty("page").GetString(), page.GetProperty("before").GetInt32(), page.GetProperty("after").ValueKind, page.GetProperty("added").ValueKind, page.GetProperty("removed").ValueKind));
        Assert.Contains("known only after running", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Dry run: 1 page has 0 connections now; the new ones are known only after running. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ADryRunOfTwoPages_AddsTheirCounts()
    {
        Assert.Equal(
            "Dry run: 2 pages have 40 connections now; the new ones are known only after running. Nothing was changed.",
            WriteToolHarness.Text(await Generate([NewPage, FullPage], dryRun: true), 0));
    }

    [Fact]
    public async Task ACommit_ListsTheConnectionsAddedAndRemoved()
    {
        CallToolResult result = await Generate([NewPage, FullPage]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_connection_generate", false, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("truncated").GetBoolean(), data.GetProperty("hint").ValueKind));
        JsonElement page = data.GetProperty("pages")[0];
        Assert.Equal((0, 9, 9, 0), (page.GetProperty("before").GetInt32(), page.GetProperty("after").GetInt32(), page.GetProperty("added").GetArrayLength(), page.GetProperty("removed").GetArrayLength()));
        JsonElement first = page.GetProperty("added")[0];
        Assert.Equal(("+A1-XG9:?1", JsonValueKind.Null, "=GAA+A1-?QA1", "11"), (first.GetProperty("from").GetProperty("device").GetString(), first.GetProperty("from").GetProperty("pin").ValueKind, first.GetProperty("to").GetProperty("device").GetString(), first.GetProperty("to").GetProperty("pin").GetString()));
        Assert.Equal("Generated connections on 2 pages: 9 added, 0 removed; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (ConnectionGenerateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.ConnectionGenerateExecute).Args!;
        Assert.Equal([NewPage, FullPage], executed.Pages);
        Assert.Equal("f1", executed.Fingerprint);
        Assert.Equal(TimeSpan.FromSeconds(64), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task PagesAlreadyUpToDate_AreCommittedWithAHint()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionGenerateExecute] = _ => Data(new ConnectionGeneratePage { Page = FullPage, Before = 40, After = 40 });

        CallToolResult result = await Generate([FullPage]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.Contains("up to date", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Generated connections on 1 page: 0 added, 0 removed; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task MoreThan100Changes_AreTruncatedWithTheTotal()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionGenerateExecute] = _ => Data(
            new ConnectionGeneratePage { Page = NewPage, After = 120, AddedCount = 120, Added = [.. Enumerable.Range(1, 98).Select(i => Wire("-KF" + i, "A1", "-KF" + i, "A2"))] },
            new ConnectionGeneratePage { Page = FullPage, Before = 40, After = 38, RemovedCount = 2, Removed = [Wire("-FC1", "2", "-QA1", "1"), Wire("-QA1", "2", "-MA1", "U1")] });

        JsonElement data = WriteToolHarness.Structured(await Generate([NewPage, FullPage]));

        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("Listed 100 of 122 added and removed connections", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecoveredCommit_ShowsTheCountsBeforeAndPointsToConnectionList()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Generate([NewPage]);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", true, false), (data.GetProperty("execution").GetString(), data.GetProperty("recovered").GetBoolean(), data.GetProperty("dryRun").GetBoolean()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("pages")[0].GetProperty("after").ValueKind);
        Assert.Contains("eplan_connection_list", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Generated connections on 1 page; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task MissingPages_FromThePlan_AreNotStarted()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionGeneratePlan] = _ => throw new BridgeCallException(new BridgeError
        {
            Reason = ErrorReason.PageNotFound,
            Message = "No page has these names.",
            Metadata = new Dictionary<string, string> { ["missing"] = "=X/1\n=X/2" },
        });

        JsonElement error = WriteToolHarness.Error(await Generate(["=X/1", NewPage, "=X/2"]));

        Assert.Equal(("PAGE_NOT_FOUND", "not_started", "=X/1\n=X/2"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("missing").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    public async Task ABridgeError_KeepsItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution };

        JsonElement error = WriteToolHarness.Error(await Generate([NewPage]));

        Assert.Equal((reason, execution), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Generate([NewPage]));

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(" ", 1, "'project' is required")]
    [InlineData("Line1", 0, "'pages' must hold 1 to 50 full page names.")]
    [InlineData("Line1", 51, "'pages' must hold 1 to 50 full page names.")]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked(string project, int count, string violation)
    {
        string[] pages = [.. Enumerable.Range(1, count).Select(n => "=A/" + n)];

        JsonElement error = WriteToolHarness.Error(await ConnectionWriteTools.ConnectionGenerateAsync(_harness.Pipeline, project, pages, false, TestContext.Current.CancellationToken));

        Assert.Contains(violation, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    [Fact]
    public void TheApprovalLines_SayThatConnectionsMayBeAddedOrRemoved()
    {
        var change = new PlannedChange { Action = "update", Kind = "page", Name = NewPage, Property = "connections", OldValue = "0" };

        Assert.Equal(["generate connections on page =GAA+A1&EFS1/60 (now 0; may add or remove connections)"], WritePipeline.ChangeLines(new WritePlan { Changes = [change] }));
        Assert.Equal("Generate connections on page =GAA+A1&EFS1/60 (now 0; may add or remove connections)", ApprovalTexts.Change(change, zh: false));
        Assert.Equal("生成页 =GAA+A1&EFS1/60 上的连接（现有 0 个；可能新增或删除连接）", ApprovalTexts.Change(change, zh: true));
    }

    private static ConnectionGenerateData Data(params ConnectionGeneratePage[] pages) => new() { Project = "Line1", Pages = [.. pages] };

    private static ChangedConnection Wire(string from, string? fromPin, string to, string? toPin) =>
        new() { From = new ConnectionEnd { Device = from, Pin = fromPin }, To = new ConnectionEnd { Device = to, Pin = toPin } };

    private Task<CallToolResult> Generate(string[] pages, bool dryRun = false) =>
        ConnectionWriteTools.ConnectionGenerateAsync(_harness.Pipeline, "Line1", pages, dryRun, TestContext.Current.CancellationToken);
}
