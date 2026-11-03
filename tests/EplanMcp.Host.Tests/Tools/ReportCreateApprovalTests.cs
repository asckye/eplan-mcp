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
}
