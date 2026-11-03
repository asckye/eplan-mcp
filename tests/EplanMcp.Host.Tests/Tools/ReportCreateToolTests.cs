using System.Globalization;
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
        _harness.Bridge.Handlers[OperationNames.ReportCreatePlan] = args => Plan((ReportCreateArgs)args!);
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
    public async Task Execute_ForwardsOpaqueSchemeDataUnchangedToPlanAndExecution()
    {
        const string filterData = "<Scheme>\r\n\t\u0007\u202e原样\"&</Scheme>";
        string sortData = new('s', 4001);
        const string functionFilterData = "function-filter\r\nvalue";
        const string functionSortData = "";

        JsonElement result = WriteToolHarness.Structured(await ReportCreateTools.CreateAsync(_harness.Pipeline, new RecordedProgress(),
            "Line1", "TerminalDiagram", "F13_008", "+A1&EMA2/102", "Location designation_A3", "DT (full)",
            filter_scheme_data: filterData, sort_scheme_data: sortData,
            function_filter_scheme_data: functionFilterData, function_sort_scheme_data: functionSortData,
            dry_run: false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("committed", result.GetProperty("execution").GetString());
        foreach (string operation in new[] { OperationNames.ReportCreatePlan, OperationNames.ReportCreateExecute })
        {
            var args = (ReportCreateArgs)Assert.Single(_harness.Bridge.Calls, c => c.Operation == operation).Args!;
            Assert.Equal(("TerminalDiagram", "F13_008", "Location designation_A3"), (args.Type, args.Form, args.FilterScheme));
            Assert.Equal((filterData, sortData, functionFilterData, functionSortData),
                (args.FilterSchemeData, args.SortSchemeData, args.FunctionFilterSchemeData, args.FunctionSortSchemeData));
        }
        string change = result.GetProperty("changes")[0].GetString()!;
        Assert.Contains("filter_scheme_data: " + filterData.Length.ToString(CultureInfo.InvariantCulture) + " characters", change, StringComparison.Ordinal);
        Assert.Contains("sort_scheme_data: 4001 characters", change, StringComparison.Ordinal);
        Assert.Contains("function_filter_scheme_data: " + functionFilterData.Length.ToString(CultureInfo.InvariantCulture) + " characters", change, StringComparison.Ordinal);
        Assert.Contains("function_sort_scheme_data: 0 characters", change, StringComparison.Ordinal);
        Assert.DoesNotContain(filterData, change, StringComparison.Ordinal);
        Assert.DoesNotContain(sortData, change, StringComparison.Ordinal);
        Assert.DoesNotContain(functionFilterData, change, StringComparison.Ordinal);
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
        JsonElement result = WriteToolHarness.Error(await ReportCreateTools.CreateAsync(_harness.Pipeline, new RecordedProgress(), "", "invalid", "", "&AAB1/102", " ", new string('x', 501), null,
            dry_run: false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.InvalidArgument, result.GetProperty("reason").GetString());
        foreach (string field in new[] { "project", "type", "form", "filter_scheme", "sort_scheme" })
        { Assert.Contains(field, result.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal); }
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private WritePlan Plan(ReportCreateArgs args)
    {
        WritePlan plan = _harness.Plan([new PlannedChange { Action = "create", Kind = "report", Name = "&AAB1/102", Property = "TableOfContents", NewValue = "F06_002" }]);
        plan.Details = new() { ["filter_scheme"] = "Location designation_A3", ["sort_scheme"] = "DT (full)" };
        foreach ((string field, string? data) in new[]
        {
            ("filter_scheme_data", args.FilterSchemeData), ("sort_scheme_data", args.SortSchemeData),
            ("function_filter_scheme_data", args.FunctionFilterSchemeData), ("function_sort_scheme_data", args.FunctionSortSchemeData),
        })
        {
            if (data is not null)
            { plan.Details[field + "_length"] = data.Length.ToString(CultureInfo.InvariantCulture); }
        }
        return plan;
    }

    private Task<CallToolResult> Create(bool dryRun = false) => ReportCreateTools.CreateAsync(_harness.Pipeline, new RecordedProgress(),
        "Line1", "TableOfContents", "F06_002", "&AAB1/102", "Location designation_A3", "DT (full)", "Directory",
        dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
