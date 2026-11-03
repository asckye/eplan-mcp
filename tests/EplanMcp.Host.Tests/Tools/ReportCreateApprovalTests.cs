using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ReportCreateApprovalTests
{
    [Fact]
    public void Approval_NamesReportTypeFormFirstPageAndBothSchemes()
    {
        var change = new PlannedChange { Action = "create", Kind = "report", Name = "&AAB1/102", Property = "TableOfContents", NewValue = "F06_002" };
        var details = new Dictionary<string, string> { ["filter_scheme"] = "Location designation_A3", ["sort_scheme"] = "DT (full)" };

        Assert.Equal("create TableOfContents report (F06_002) from &AAB1/102; filter: Location designation_A3; sort: DT (full)",
            Assert.Single(WritePipeline.ChangeLines(new WritePlan { Changes = [change], Details = details })));
        Assert.Equal("Create TableOfContents report (F06_002) from &AAB1/102; filter: Location designation_A3; sort: DT (full)", ApprovalTexts.Change(change, false, details));
        Assert.Contains("从 &AAB1/102 创建 TableOfContents 报表（F06_002）", ApprovalTexts.Change(change, true, details), StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_OnlyShowsLengthsForAllSchemeDataIncludingEmptyData()
    {
        var change = new PlannedChange { Action = "create", Kind = "report", Name = "+A1&EMA2/102", Property = "TerminalDiagram", NewValue = "F13_008" };
        var details = new Dictionary<string, string>
        {
            ["filter_scheme"] = "Location designation_A3",
            ["sort_scheme"] = "DT (full)",
            ["filter_scheme_data_length"] = "25",
            ["sort_scheme_data_length"] = "4001",
            ["function_filter_scheme_data_length"] = "19",
            ["function_sort_scheme_data_length"] = "0",
            ["filter_scheme_data"] = "private-filter-data",
            ["sort_scheme_data"] = "private-sort-data",
            ["function_filter_scheme_data"] = "private-function-filter-data",
            ["function_sort_scheme_data"] = "private-function-sort-data",
        };

        foreach (string text in new[]
        {
            Assert.Single(WritePipeline.ChangeLines(new WritePlan { Changes = [change], Details = details })),
            ApprovalTexts.Change(change, false, details), ApprovalTexts.Change(change, true, details),
        })
        {
            Assert.Contains("Location designation_A3", text, StringComparison.Ordinal);
            Assert.Contains("DT (full)", text, StringComparison.Ordinal);
            Assert.Contains("filter_scheme_data: 25 characters", text, StringComparison.Ordinal);
            Assert.Contains("sort_scheme_data: 4001 characters", text, StringComparison.Ordinal);
            Assert.Contains("function_filter_scheme_data: 19 characters", text, StringComparison.Ordinal);
            Assert.Contains("function_sort_scheme_data: 0 characters", text, StringComparison.Ordinal);
            Assert.DoesNotContain("private-", text, StringComparison.Ordinal);
        }
    }
}
