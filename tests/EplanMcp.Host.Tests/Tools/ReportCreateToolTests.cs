using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ReportCreateToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);

    public ReportCreateToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.ReportCreatePlan] = _ => Plan();
        _harness.Bridge.Handlers[OperationNames.ReportCreateExecute] = _ => new ReportCreateData
        { Pages = ["&AAB1/102", "&AAB1/103"], NewPages = ["&AAB1/103"] };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_PlansWithoutExecution()
    {
        JsonElement result = WriteToolHarness.Structured(await Create(dryRun: true));

        Assert.Equal("not_started", result.GetProperty("execution").GetString());
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Equal("f1", result.GetProperty("fingerprint").GetString());
        Assert.Equal("create TableOfContents report (F06_002) from &AAB1/102; filter: Location designation_A3; sort: DT (full)", result.GetProperty("changes")[0].GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("pages").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("new_pages").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("count").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("undo").ValueKind);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task Default_IsPreviewWithoutExecution()
    {
        JsonElement result = WriteToolHarness.Structured(await ReportCreateTools.CreateAsync(_harness.Pipeline, new RecordedProgress(),
            "Line1", "TableOfContents", "F06_002", "&AAB1/102", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("not_started", result.GetProperty("execution").GetString());
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task Execute_ForwardsAllSettingsAndFingerprintAndReportsPages()
    {
        JsonElement result = WriteToolHarness.Structured(await Create());
        var args = (ReportCreateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.ReportCreateExecute).Args!;

        Assert.Equal(("Line1", "TableOfContents", "F06_002", "&AAB1/102", "Location designation_A3", "DT (full)", "Directory", "f1"),
            (args.Project, args.Type, args.Form, args.FirstPage, args.FilterScheme, args.SortScheme, args.PageDescription, args.Fingerprint));
        Assert.Equal(["&AAB1/102", "&AAB1/103"], result.GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(["&AAB1/103"], result.GetProperty("new_pages").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(("committed", "TableOfContents", "F06_002", 2, "AI: eplan_report_create"),
            (result.GetProperty("execution").GetString(), result.GetProperty("type").GetString(), result.GetProperty("form").GetString(), result.GetProperty("count").GetInt32(), result.GetProperty("undo").GetString()));
        Assert.Equal(TimeSpan.FromSeconds(300), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("hint").ValueKind);
    }

    [Fact]
    public async Task ExistingReports_WithNoNewPages_ReturnsOverwriteHint()
    {
        _harness.Bridge.Handlers[OperationNames.ReportCreateExecute] = _ => new ReportCreateData
        { Pages = ["&AAB1/102"], Warning = "Readback warning" };

        JsonElement result = WriteToolHarness.Structured(await Create());

        Assert.Equal("committed", result.GetProperty("execution").GetString());
        Assert.Empty(result.GetProperty("new_pages").EnumerateArray());
        Assert.Equal("Existing report pages were overwritten; no new page names were added. Readback warning", result.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Recovery_DoesNotInventPageNamesOrCount()
    {
        _harness.Bridge.WriteRecovered = true;
        CallToolResult call = await Create();
        JsonElement result = WriteToolHarness.Structured(call);

        Assert.True(result.GetProperty("recovered").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("pages").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("new_pages").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("count").ValueKind);
        Assert.Contains("eplan_page_list", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("0 report", WriteToolHarness.Text(call, 0), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_CleansProjectDataAndKeepsReadbackWarning()
    {
        _harness.Bridge.Handlers[OperationNames.ReportCreateExecute] = _ => new ReportCreateData
        { Pages = ["&AAB1/102\u202e"], NewPages = ["&AAB1/102\u202e"], Warning = "Readback incomplete\u202e" };

        JsonElement result = WriteToolHarness.Structured(await Create());

        Assert.Equal("F06_002", result.GetProperty("form").GetString());
        Assert.Equal("&AAB1/102", result.GetProperty("pages")[0].GetString());
        Assert.Equal("&AAB1/102", result.GetProperty("new_pages")[0].GetString());
        Assert.Equal("Readback incomplete", result.GetProperty("hint").GetString());
    }

    [Theory]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.InvalidArgument, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    public async Task BridgeFailure_PreservesExecutionAndOriginalSchemeMessage(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError
        { Reason = reason, Message = "Scheme rejected", Execution = execution, Metadata = new() { ["eplanMessage"] = "InvalidScheme: Location designation_A3" } };

        JsonElement result = WriteToolHarness.Error(await Create());

        Assert.Equal((reason, execution), (result.GetProperty("reason").GetString(), result.GetProperty("execution").GetString()));
        Assert.Equal("InvalidScheme: Location designation_A3", result.GetProperty("metadata").GetProperty("eplanMessage").GetString());
    }

    [Fact]
    public async Task OutsideScratch_DeniesWrite()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement result = WriteToolHarness.Error(await Create());

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (result.GetProperty("reason").GetString(), result.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task InvalidArguments_AreReturnedWithoutCallingBridge()
    {
        JsonElement result = WriteToolHarness.Error(await ReportCreateTools.CreateAsync(_harness.Pipeline, new RecordedProgress(), "", "invalid", "", "&AAB1/102", " ", new string('x', 501), null, false, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.InvalidArgument, result.GetProperty("reason").GetString());
        foreach (string field in new[] { "project", "type", "form", "filter_scheme", "sort_scheme" })
        { Assert.Contains(field, result.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal); }
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private WritePlan Plan()
    {
        WritePlan plan = _harness.Plan([new PlannedChange { Action = "create", Kind = "report", Name = "&AAB1/102", Property = "TableOfContents", NewValue = "F06_002" }]);
        plan.Details = new() { ["filter_scheme"] = "Location designation_A3", ["sort_scheme"] = "DT (full)" };
        return plan;
    }

    private Task<CallToolResult> Create(bool dryRun = false) => ReportCreateTools.CreateAsync(_harness.Pipeline, new RecordedProgress(),
        "Line1", "TableOfContents", "F06_002", "&AAB1/102", "Location designation_A3", "DT (full)", "Directory", dryRun, TestContext.Current.CancellationToken);
}
