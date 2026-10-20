using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_page_update through the real write pipeline against a fake Bridge (docs/tools/eplan_page_update.md, sections 3, 4, 7).</summary>
[Trait("Size", "Medium")]
public sealed class PageUpdateToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new();
    private List<PlannedChange> _changes = [Change("=GAA+A1&EFS1/3", "电源", "主回路")];

    public PageUpdateToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.PageUpdatePlan] = _ => _harness.Plan(_changes);
        _harness.Bridge.Handlers[OperationNames.PageUpdateExecute] = _ => new PageUpdateData
        {
            Project = "Line1",
            Language = "zh_CN",
            Pages = [new PageDescriptionChange { Name = "=GAA+A1&EFS1/3", Description = "主回路", Previous = "电源" }],
            Unchanged = 1,
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ListsThePlannedChangesWithTheirLanguage()
    {
        CallToolResult result = await Update(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", "zh_CN", 1), (data.GetProperty("execution").GetString(), data.GetProperty("language").GetString(), data.GetProperty("unchanged").GetInt32()));
        JsonElement page = data.GetProperty("pages")[0];
        Assert.Equal(("主回路", "电源"), (page.GetProperty("description").GetString(), page.GetProperty("previous").GetString()));
        Assert.Equal("Dry run: would update 1 page. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ACommit_ReturnsWhatEplanReadBack()
    {
        CallToolResult result = await Update(language: "zh_CN");

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_page_update", 1), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("unchanged").GetInt32()));
        Assert.Equal("Updated the descriptions of 1 page; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (PageUpdateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.PageUpdateExecute).Args!;
        Assert.Equal(("f1", "zh_CN", string.Empty), (executed.Fingerprint, executed.Language, executed.Pages[1].Description));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task NothingToChange_WritesNothingAndSaysSo()
    {
        _changes = [];

        CallToolResult result = await Update();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", 2, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("unchanged").GetInt32(), data.GetProperty("undo").ValueKind));
        Assert.Equal("Nothing to change: the 2 pages already have these descriptions.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ARecoveredCommit_ListsThePlanAndPointsToPageGet()
    {
        _harness.Bridge.WriteRecovered = true;

        JsonElement data = WriteToolHarness.Structured(await Update());

        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal("主回路", data.GetProperty("pages")[0].GetProperty("description").GetString());
        Assert.Contains("eplan_page_get", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked()
    {
        JsonElement error = WriteToolHarness.Error(await PageWriteTools.PageUpdateAsync(_harness.Pipeline, string.Empty, [], "zh-CN", false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(3, error.GetProperty("metadata").GetProperty("violations").GetString()!.Split('\n').Length);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private static PlannedChange Change(string page, string? before, string? after) =>
        new() { Action = "update", Kind = "page", Name = page, Property = "description (zh_CN)", OldValue = before, NewValue = after };

    private Task<CallToolResult> Update(bool dryRun = false, string? language = null) => PageWriteTools.PageUpdateAsync(
        _harness.Pipeline,
        "Line1",
        [new PageWriteTools.PageText { Name = "=GAA+A1&EFS1/3", Description = "主回路" }, new PageWriteTools.PageText { Name = "=GAA+A1&EFS1/4", Description = string.Empty }],
        language,
        dryRun,
        cancellationToken: TestContext.Current.CancellationToken);
}
