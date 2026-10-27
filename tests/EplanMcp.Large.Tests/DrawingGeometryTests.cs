using System.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class DrawingGeometryTests(LiveEplan eplan)
{
    [Fact]
    public async Task FixtureCircuitPage_ReadsGridSizeAndFullPlacementBounds()
    {
        string? variantBin = Environment.GetEnvironmentVariable("EPLANMCP_DRAWING_VARIANT_BIN");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(variantBin), "Set EPLANMCP_DRAWING_VARIANT_BIN and build tests/EplanMcp.Large.DrawingHelper; see README.md.");
        string page = (await eplan.CallAsync("eplan_page_list", new() { ["page_type"] = "Circuit" }))
            .GetProperty("pages").EnumerateArray().First().GetProperty("name").GetString()!;
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "drawing");
        string copy = Path.Combine(folder, LiveEplan.ProjectName);
        OfflineHost.CopyFixture(LiveEplan.ScratchRoot(), copy);
        string exe = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.DrawingHelper", "EplanMcp.Large.DrawingHelper.exe");
        Assert.True(File.Exists(exe), "Build tests/EplanMcp.Large.DrawingHelper in the configuration of these tests.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { variantBin!, copy + ".elk", page })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            throw;
        }

        Assert.True(process.ExitCode == 0, await stderr);
        PageDrawingGeometry geometry = JsonConvert.DeserializeObject<PageDrawingGeometry>(await stdout)!;
        Assert.Equal(page, geometry.Page);
        Assert.True(double.IsFinite(geometry.GridSize) && geometry.GridSize > 0);
        Assert.NotNull(geometry.PageSize);
        Assert.All(geometry.PageSize, v => Assert.True(double.IsFinite(v) && v > 0));
        Assert.NotNull(geometry.PlotFrameSize);
        Assert.NotEmpty(geometry.Placements);
        Assert.Contains(geometry.Placements, p => p.Bounds is not null);
        Assert.All(geometry.Placements, p =>
        {
            Assert.All(p.Location, v => Assert.True(double.IsFinite(v)));
            Assert.True(p.Bounds is null || DrawingCoordinates.ValidBounds(p.Bounds));
            Assert.Equal(copy, p.Locator.Project, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(page, p.Locator.Page);
            Assert.Matches("^[0-9a-f]{64}$", p.Locator.Digest);
        });
        Directory.Delete(folder, recursive: true);
    }
}
