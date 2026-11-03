using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>Reads A84's known table of contents through the public tool without writing the fixture.</summary>
[Trait("Size", "Large")]
public sealed class ReportReadTests(LiveEplan eplan)
{
    [Fact]
    public async Task SampleTableOfContents_ExposesReportBlockWithoutSchemeData()
    {
        JsonElement page = await eplan.CallAsync("eplan_page_get", new() { ["project"] = eplan.ProjectPath, ["page"] = "&AAB1/1" });
        JsonElement report = page.GetProperty("report");
        Assert.Equal("TableOfContents", report.GetProperty("type").GetString());
        Assert.Equal("F06_002", report.GetProperty("form").GetString());
        Assert.All(report.EnumerateObject(), field => Assert.DoesNotContain("data", field.Name, StringComparison.OrdinalIgnoreCase));
    }
}
