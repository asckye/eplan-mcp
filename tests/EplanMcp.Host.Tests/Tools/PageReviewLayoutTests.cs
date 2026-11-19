using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Tests;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PageReviewLayoutTests
{
    private sealed class Reader : PageReviewOperation.IReader
    {
        public (PageScene Scene, JObject Actual) Read(string project, string page, JObject request)
        {
            var scene = new PageScene { Page = new() { Page = page, GridSize = 4, PageSize = [80, 80] }, Project = new() { Name = project } };
            scene.Layout.MinimumTextHeightMm = 1;
            scene.ConfigLayoutMembers.Add("minimumTextHeightMm");
            return (scene, JObject.Parse("""{"devices":[],"connections":[],"interruptions":[],"messages":[],"texts":[],"cables":[]}"""));
        }
    }

    private static PageReviewRulePackFixture Fixture()
    {
        var fixture = new PageReviewRulePackFixture();
        var operation = new PageReviewOperation(new Reader());
        fixture.Bridge.Handlers[OperationNames.PageReview] = request =>
        {
            var result = (JObject)operation.Execute((object)JObject.Parse(JsonSerializer.Serialize(request)))!;
            return JsonNode.Parse(result.ToString())!.AsObject();
        };
        return fixture;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("\"2\"")]
    public async Task InvalidTextHeight_WarnsAndKeepsFileValueWhileValidSpacingIsApplied(string height)
    {
        using var fixture = Fixture();
        await PageReviewLayoutPluginFixture.InstallAsync(fixture.Locations, new()
        {
            ["review.layout.parallel_spacing_mm"] = 100,
            ["review.layout.minimum_text_height_mm"] = JsonNode.Parse(height),
        }, PageReviewRulePackFixture.Cancel);
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync(checks: ["net_overlap"]));
        JsonElement request = JsonSerializer.SerializeToElement(fixture.Bridge.LastArgs).GetProperty("layout");
        Assert.Equal("parallelSpacingMm", Assert.Single(request.EnumerateObject()).Name);
        Assert.Equal(100, request.GetProperty("parallelSpacingMm").GetDouble());
        JsonElement layout = result.GetProperty("layout");
        Assert.Equal(100, layout.GetProperty("parallelSpacingMm").GetDouble());
        Assert.Equal(1, layout.GetProperty("minimumTextHeightMm").GetDouble());
        Assert.Equal("plugin:" + PageReviewLayoutPluginFixture.Id, layout.GetProperty("sources").GetProperty("parallelSpacingMm").GetString());
        Assert.Equal("config", layout.GetProperty("sources").GetProperty("minimumTextHeightMm").GetString());
        Assert.Equal("default", layout.GetProperty("sources").GetProperty("marginMm").GetString());
        string? warning = Assert.Single(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray()).GetString();
        Assert.Contains(PageReviewLayoutPluginFixture.Id, warning, StringComparison.Ordinal);
        Assert.Contains("review.layout.minimum_text_height_mm", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExhaustedMargin_KeepsBridgeSourceAndAddsPluginToWarning()
    {
        using var fixture = Fixture();
        await PageReviewLayoutPluginFixture.InstallAsync(fixture.Locations, new()
        {
            ["review.layout.margin_mm"] = 40,
            ["review.layout.minimum_text_height_mm"] = 2.5,
        }, PageReviewRulePackFixture.Cancel);
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync(checks: ["text_format"]));
        JsonElement layout = result.GetProperty("layout");
        Assert.Equal(10, layout.GetProperty("marginMm").GetDouble());
        Assert.Equal("default", layout.GetProperty("sources").GetProperty("marginMm").GetString());
        Assert.Equal("plugin:" + PageReviewLayoutPluginFixture.Id, layout.GetProperty("sources").GetProperty("minimumTextHeightMm").GetString());
        Assert.False(result.TryGetProperty("layoutWarnings", out _));
        string? warning = Assert.Single(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray()).GetString();
        Assert.Contains("review.layout.margin_mm", warning, StringComparison.Ordinal);
        Assert.Contains(PageReviewLayoutPluginFixture.Id, warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public async Task OutOfRangeSpacing_IsOmittedWhileZeroMarginIsAccepted(double spacing)
    {
        using var fixture = Fixture();
        await PageReviewLayoutPluginFixture.InstallAsync(fixture.Locations, new()
        {
            ["review.layout.parallel_spacing_mm"] = spacing,
            ["review.layout.margin_mm"] = 0,
        }, PageReviewRulePackFixture.Cancel);
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync(checks: ["net_overlap"]));
        JsonElement request = JsonSerializer.SerializeToElement(fixture.Bridge.LastArgs).GetProperty("layout");
        Assert.Equal("marginMm", Assert.Single(request.EnumerateObject()).Name);
        JsonElement layout = result.GetProperty("layout");
        Assert.Equal(JsonValueKind.Null, layout.GetProperty("parallelSpacingMm").ValueKind);
        Assert.Equal("default", layout.GetProperty("sources").GetProperty("parallelSpacingMm").GetString());
        Assert.Equal(0, layout.GetProperty("marginMm").GetDouble());
        Assert.Equal("plugin:" + PageReviewLayoutPluginFixture.Id, layout.GetProperty("sources").GetProperty("marginMm").GetString());
        string? warning = Assert.Single(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray()).GetString();
        Assert.Contains("review.layout.parallel_spacing_mm", warning, StringComparison.Ordinal);
        Assert.Contains(PageReviewLayoutPluginFixture.Id, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoThresholds_OmitsLayoutFromRequestAndReportsBridgeValues()
    {
        using var fixture = Fixture();
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync());
        Assert.False(JsonSerializer.SerializeToElement(fixture.Bridge.LastArgs).TryGetProperty("layout", out _));
        JsonElement layout = result.GetProperty("layout");
        Assert.Equal(JsonValueKind.Null, layout.GetProperty("parallelSpacingMm").ValueKind);
        Assert.Equal("default", layout.GetProperty("sources").GetProperty("parallelSpacingMm").GetString());
        Assert.Equal("config", layout.GetProperty("sources").GetProperty("minimumTextHeightMm").GetString());
    }

    [Fact]
    public async Task DraftMarginOutsideScratch_FallsBackWithoutRefusingReview()
    {
        using var fixture = Fixture();
        fixture.ProjectPath = Path.Combine(fixture.Locations.Root, "production", "Line1");
        await PageReviewLayoutPluginFixture.InstallAsync(fixture.Locations, new() { ["review.layout.margin_mm"] = 8 }, PageReviewRulePackFixture.Cancel);
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync(checks: ["overlaps"]));
        Assert.False(JsonSerializer.SerializeToElement(fixture.Bridge.LastArgs).TryGetProperty("layout", out _));
        Assert.Equal("default", result.GetProperty("layout").GetProperty("sources").GetProperty("marginMm").GetString());
        string? warning = Assert.Single(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray()).GetString();
        Assert.Contains("review.layout.margin_mm", warning, StringComparison.Ordinal);
        Assert.Contains(PageReviewLayoutPluginFixture.Id, warning, StringComparison.Ordinal);
    }
}
