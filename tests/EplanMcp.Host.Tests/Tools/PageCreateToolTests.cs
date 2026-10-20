using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_page_create through the real write pipeline against a fake Bridge, with the audit and suspensions in a
/// temporary data directory (docs/tools/eplan_page_create.md, sections 3, 4 and 7).
/// </summary>
[Trait("Size", "Medium")]
public sealed class PageCreateToolTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-page-create-").FullName);
    private readonly FakeBridgeClient _bridge = new();
    private readonly WritePipeline _pipeline;
    private string _projectPath;

    public PageCreateToolTests()
    {
        _projectPath = Path.Combine(_locations.DefaultScratch, "Line1");
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", Environment.ProcessId)],
            new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "b", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3" } },
            null);
        _bridge.Handlers[OperationNames.PageCreatePlan] = args => new WritePlan
        {
            Project = "Line1",
            ProjectPath = _projectPath,
            Changes = [.. ((PageCreateArgs)args!).Pages.Select(p => new PlannedChange { Action = "create", Kind = "page", Name = p.Name, NewValue = p.PageType ?? "Circuit" })],
            Fingerprint = "f1",
        };
        _bridge.Handlers[OperationNames.PageCreateExecute] = args => new PageCreateData
        {
            Project = "Line1",
            Pages = [.. ((PageCreateArgs)args!).Pages.Select(p => new PageSummary { Name = p.Name, Type = p.PageType ?? "Circuit", Description = p.Description })],
        };
        var audit = new AuditLog(_locations.Audit, TimeProvider.System);
        _pipeline = new WritePipeline(
            _bridge,
            HostConfig.Defaults(_locations),
            SuspensionStore.For(_locations),
            audit,
            new DanglingWrites(_bridge, SuspensionStore.For(_locations), audit, TimeProvider.System, NullLogger<DanglingWrites>.Instance),
            new ApprovalService([], HostConfig.Defaults(_locations), TimeProvider.System, NullLogger<ApprovalService>.Instance),
            new BackupFolders(_locations, TimeProvider.System),
            TimeProvider.System,
            NullLogger<WritePipeline>.Instance);
    }

    public void Dispose() => Directory.Delete(_locations.Root, recursive: true);

    [Fact]
    public async Task ADryRun_ListsThePlannedPagesAndNeverExecutes()
    {
        CallToolResult result = await Create([Page("=GAA+A1&EFS1/5", "overview", "电源")], dryRun: true);

        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(("not_started", true, JsonValueKind.Null), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("undo").ValueKind));
        JsonElement page = data.GetProperty("pages")[0];
        Assert.Equal(("=GAA+A1&EFS1/5", "Overview", "电源"), (page.GetProperty("name").GetString(), page.GetProperty("type").GetString(), page.GetProperty("description").GetString()));
        Assert.Equal("Dry run: would create 1 page. Nothing was changed.", Text(result, 0));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task ACommit_ReturnsThePagesReadBackAndTheUndoStep()
    {
        CallToolResult result = await Create([Page("=GAA+A1&EFS1/5"), Page("=GAA+A1&EFS1/6", "GRAPHICS")], language: "zh_CN");

        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(("committed", "AI: eplan_page_create", false), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("recovered").GetBoolean()));
        Assert.Equal(["Circuit", "Graphics"], data.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("type").GetString()));
        Assert.Equal("Created 2 pages; Ctrl+Z in EPLAN undoes this step.", Text(result, 0));
        var executed = (PageCreateArgs)_bridge.Calls.Last(c => c.Operation == OperationNames.PageCreateExecute).Args!;
        Assert.Equal(("Line1", "f1", "zh_CN"), (executed.Project, executed.Fingerprint, executed.Language));
        Assert.Equal(TimeSpan.FromSeconds(64), Assert.Single(_bridge.Writes).Limit);
    }

    [Fact]
    public async Task ARecoveredCommit_ListsThePlanAndPointsToPageList()
    {
        _bridge.WriteRecovered = true;

        JsonElement data = (await Create([Page("=GAA+A1&EFS1/5")])).StructuredContent!.Value;

        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal("=GAA+A1&EFS1/5", data.GetProperty("pages")[0].GetProperty("name").GetString());
        Assert.Contains("eplan_page_list", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWarningAfterTheCommit_IsTheHint()
    {
        _bridge.Handlers[OperationNames.PageCreateExecute] = _ => new PageCreateData { Project = "Line1", Pages = [new PageSummary { Name = "=A/1", Type = "Circuit" }], Warning = "undo step not closed" };

        JsonElement data = (await Create([Page("=A/1")])).StructuredContent!.Value;

        Assert.Equal(("committed", "undo step not closed"), (data.GetProperty("execution").GetString(), data.GetProperty("hint").GetString()));
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDeniedAndNeverExecutes()
    {
        _projectPath = Path.Combine(_locations.Root, "Projects", "Line1");

        JsonElement error = Error(await Create([Page("=A/1")]));

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task ABridgeError_KeepsItsExecutionState()
    {
        _bridge.WriteFailure = new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "dropped", Execution = ExecutionState.RolledBack };

        JsonElement error = Error(await Create([Page("=A/1")]));

        Assert.Equal(("INVALID_ARGUMENT", "rolled_back"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    [Theory]
    [InlineData(" ", 1, null, "'project' is required")]
    [InlineData("Line1", 0, null, "'pages' must hold 1 to 50 pages.")]
    [InlineData("Line1", 51, null, "'pages' must hold 1 to 50 pages.")]
    [InlineData("Line1", 1, "chinese", "'language' must be a language code")]
    public async Task HostChecks_FailBeforeTheBridgeIsAsked(string project, int count, string? language, string violation)
    {
        PageWriteTools.NewPage[] pages = [.. Enumerable.Range(1, count).Select(n => Page("=A/" + n))];

        JsonElement error = Error(await PageWriteTools.PageCreateAsync(_pipeline, project, pages, language, false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("reason").GetString());
        Assert.Contains(violation, error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _bridge.Invocations);
    }

    [Fact]
    public void TheLimit_GrowsWithThePagesUpToFiveMinutes()
    {
        Assert.Equal((62, 160, 300), (PageWriteTools.Limit(1).TotalSeconds, PageWriteTools.Limit(50).TotalSeconds, PageWriteTools.Limit(200).TotalSeconds));
    }

    [Fact]
    public void TheApprovalLines_ShowTypeAndDescription()
    {
        var plan = new WritePlan { Changes = [new PlannedChange { Action = "create", Kind = "page", Name = "=A/1", NewValue = "Circuit, \"电源\"" }, new PlannedChange { Action = "create", Kind = "page", Name = "=A/2" }] };

        Assert.Equal(["create page =A/1 (Circuit, \"电源\")", "create page =A/2"], WritePipeline.ChangeLines(plan));
    }

    private static PageWriteTools.NewPage Page(string name, string? type = null, string? description = null) => new() { Name = name, PageType = type, Description = description };

    private Task<CallToolResult> Create(PageWriteTools.NewPage[] pages, bool dryRun = false, string? language = null) =>
        PageWriteTools.PageCreateAsync(_pipeline, "Line1", pages, language, dryRun, cancellationToken: TestContext.Current.CancellationToken);

    private static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
    }
}
