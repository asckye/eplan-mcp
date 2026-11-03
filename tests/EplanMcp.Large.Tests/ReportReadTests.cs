using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>Reads A84's known table of contents through the public tool without writing the fixture.</summary>
[Trait("Size", "Large")]
public sealed class ReportReadTests(LiveEplan eplan)
{
    [Fact]
    public async Task SampleTableOfContents_ExposesReportBlockWithSchemeData()
    {
        JsonElement page = await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = "&AAB1/1" });
        JsonElement report = page.GetProperty("report");
        Assert.Equal("TableOfContents", report.GetProperty("type").GetString());
        Assert.Equal("F06_002", report.GetProperty("form").GetString());
        // A84: scheme names are project settings, so report_create needs the scheme data read here (the table of contents has none).
        foreach (string field in new[] { "filter_scheme_data", "sort_scheme_data", "function_filter_scheme_data", "function_sort_scheme_data" })
        { Assert.Equal(string.Empty, report.GetProperty(field).GetString()); }
    }
}
