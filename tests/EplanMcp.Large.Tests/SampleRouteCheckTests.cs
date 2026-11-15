using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Connection length spike (owner 2026-11-14, data-relationships R5): routes the sample fixture's 3D connections again in
/// the installation spaces the 16-page redraw uses and rolls back. Prints how many kept their length; changes nothing.
/// </summary>
[Trait("Size", "Large")]
[Trait("Spike", "SampleRouteCheck")]
public sealed class SampleRouteCheckTests(LiveEplan eplan)
{
    [Theory]
    [InlineData("A1")]
    [InlineData("A2")]
    public async Task RoutingAgain_KeepsTheLengthOfEveryConnectionItRoutes(string space)
    {
        string project = eplan.ProjectPath;
        string expected = Path.Combine(LiveEplan.ScratchRoot(), "fixtures", LiveEplan.ProjectName);
        Assert.True(string.Equals(Path.GetFullPath(expected), Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase),
            "SampleRouteCheck requires the open scratch/fixtures/EPLAN_Sample_Project fixture.");
        string requests = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        string request = Path.Combine(requests, "route-check-" + Guid.NewGuid().ToString("N") + ".json");
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running SampleRouteCheck.");
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new { project, space }), TestContext.Current.CancellationToken);

        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await SmallProjectExportTests.RunActionAsync("EplanMcpLargeRouteCheck", request);
            string response = await File.ReadAllTextAsync(request + ".result.json", TestContext.Current.CancellationToken);
            TestContext.Current.TestOutputHelper!.WriteLine(response);
            using JsonDocument result = JsonDocument.Parse(response);
            Assert.True(result.RootElement.GetProperty("success").GetBoolean(), response);
            JsonElement cleared = result.RootElement.GetProperty("cleared");
            JsonElement after = result.RootElement.GetProperty("after");
            // Clearing must change the lengths, or routing again proves nothing.
            Assert.True(cleared.GetProperty("same").GetInt32() < cleared.GetProperty("compared").GetInt32(), "Clearing changed no length.");
            JsonElement rolledBack = result.RootElement.GetProperty("rolledBack");
            Assert.Equal(rolledBack.GetProperty("compared").GetInt32(), rolledBack.GetProperty("same").GetInt32());
            // Live 2026-11-14: 505 of 508 came back to the same length; three in A1 could not be routed again because
            // clearing also removes the route nodes a designer set. A connection routed again must keep its length.
            Assert.All(after.GetProperty("changed").EnumerateArray(), changed =>
                Assert.False(changed.GetProperty("after").GetProperty("routed").GetBoolean(), changed.GetRawText()));
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
        }
    }
}
