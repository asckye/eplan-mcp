using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // The adapter owns and disposes the fake block; the page is borrowed.
[Trait("Size", "Small")]
public sealed class ReportReadTests
{
    [Fact]
    public void NonReportPage_HasNoSettings() => Assert.Null(EplanReportReader.Read(new Page()));

    [Fact]
    public void ReportPage_ReadsNamesOnlyAndDisposesNativeValues()
    {
        var block = new ReportBlock();
        PageReport report = Assert.IsType<PageReport>(EplanReportReader.Read(new Page { ReportBlock = block }));

        Assert.Equal("TableOfContents", report.Type);
        Assert.Equal("F06_002", report.Form);
        Assert.Equal("Location designation_A3", report.FilterScheme);
        Assert.Equal("DT (full)", report.SortScheme);
        Assert.Equal("Functions", report.FunctionFilterScheme);
        Assert.Equal("Function sort", report.FunctionSortScheme);
        Assert.Equal("=K1+A2-KE1", report.DeviceTag);
        Assert.True(report.AutomaticPageDescription);
        Assert.Equal("en_US@Contents;zh_CN@目录;", report.ManualPageDescription);
        Assert.True(block.Disposed);
        Assert.True(block.Description.Disposed);
    }

    [Theory]
    [InlineData("type", "Type")]
    [InlineData("form", "Form")]
    [InlineData("filter_scheme", "FilterScheme")]
    [InlineData("sort_scheme", "SortScheme")]
    [InlineData("function_filter_scheme", "FunctionFilterScheme")]
    [InlineData("function_sort_scheme", "FunctionSortScheme")]
    [InlineData("device_tag", "DeviceTag")]
    [InlineData("automatic_page_description", "AutomaticPageDescription")]
    [InlineData("manual_page_description", "ManualPageDescription")]
    public void FieldFailure_IsNullWithOneWarningAndKeepsAllOtherFields(string field, string property)
    {
        var block = new ReportBlock { FailField = field };
        var log = new ListLog();
        var context = new OperationContext("report-read", false, null, log);
        var report = (PageReport)context.Run(() => EplanReportReader.Read(new Page { ReportBlock = block }))!;

        JObject data = JObject.FromObject(report);
        Assert.Equal(JTokenType.Null, data[property]!.Type);
        Assert.Equal(8, data.Properties().Count(p => p.Value.Type != JTokenType.Null));
        var warning = Assert.Single(log.Entries);
        Assert.Equal(BridgeLogLevel.Warning, warning.Level);
        Assert.Contains(field, warning.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(warning.Exception);
        Assert.True(block.Disposed);
    }
}
