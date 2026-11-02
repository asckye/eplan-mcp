using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class LayoutGuardTests(LiveEplan eplan, WriteHost host, ITestOutputHelper output) : IClassFixture<WriteHost>
{
    private async Task<PageDrawingGeometry> Page(PlanStepLiveSession session)
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        Assert.Equal(host.Writable, session.Project, ignoreCase: true);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        await host.CallAsync("eplan_device_place", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["tag"] = "-LG" + Guid.NewGuid().ToString("N")[..8],
            ["x"] = 80,
            ["y"] = 160
        });
        await host.CallAsync("eplan_placement_create", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["items"] = new[] { new { kind = "text", position = (int[])[140, 100], height_mm = 3.5, contents = new Dictionary<string, string> { ["??_??"] = "Layout guard note" } } }
        });
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page! }, TestContext.Current.CancellationToken);
        Assert.NotNull(geometry.PageSize);
        return geometry;
    }
    [Fact]
    public async Task DeviceOverExistingDevice_RefusedWithFreePosition_WithoutWriting()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        PageDrawingGeometry page = await Page(session);
        DrawingPlacement device = page.Placements.First(p => p.Kind == "Function");
        JsonElement error = await host.ErrorAsync("eplan_device_place", new()
        {
            ["project"] = host.Writable,
            ["page"] = page.Page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["tag"] = "-LG" + Guid.NewGuid().ToString("N")[..8],
            ["x"] = device.Location[0],
            ["y"] = device.Location[1],
            ["dry_run"] = true,
        });
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("R2", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Suggested position", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task DrawingInTitleBlockFallbackBand_RefusedBeforeWriting()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        PageDrawingGeometry page = await Page(session);
        JsonElement error = await host.ErrorAsync("eplan_device_place", new()
        {
            ["project"] = host.Writable,
            ["page"] = page.Page,
            ["location_box"] = "+LG" + Guid.NewGuid().ToString("N")[..6],
            ["x"] = 0,
            ["y"] = 0,
            ["corner_x"] = 4,
            ["corner_y"] = 4,
            ["dry_run"] = true,
        });
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("R1", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("fallback", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task MovingTextOntoSymbol_RefusedWithSuggestion()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        PageDrawingGeometry page = await Page(session);
        DrawingPlacement text = page.Placements.First(p => p.Kind == "Text" && p.Parent is null && p.Properties.GetValueOrDefault("grouped") == "false");
        DrawingPlacement symbol = page.Placements.First(p => p.Kind == "Function");
        double x = text.Location[0] + (symbol.Bounds![0] + symbol.Bounds[2] - text.Bounds![0] - text.Bounds[2]) / 2;
        double y = text.Location[1] + (symbol.Bounds[1] + symbol.Bounds[3] - text.Bounds[1] - text.Bounds[3]) / 2;
        JsonElement error = await host.ErrorAsync("eplan_placement_update", new()
        {
            ["project"] = host.Writable,
            ["page"] = page.Page,
            ["dry_run"] = true,
            ["snap_to_grid"] = false,
            ["moves"] = new[] { new { locator = text.Locator, x_mm = x, y_mm = y } },
        });
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("R3", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Suggested position", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
    [Fact]
    public async Task UnknownTextOutline_IsMeasuredBeforeCommit_RefusalRestoresPageAndKeepsPreviousUndoStep()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        PageDrawingGeometry page = await Page(session);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string before = JsonSerializer.Serialize(page);
        double labelX = Math.Floor((page.PageSize![0] - 10) / page.GridSize) * page.GridSize - page.GridSize;
        JsonElement error = await host.ErrorAsync("eplan_device_place", new()
        {
            ["project"] = host.Writable,
            ["page"] = page.Page,
            ["library"] = "IEC_symbol",
            ["symbol"] = "K",
            ["symbol_variant"] = 0,
            ["tag"] = "-LGB" + Guid.NewGuid().ToString("N")[..16],
            ["x"] = 160,
            ["y"] = 160,
            ["display_properties"] = new[] { new { property_id = 20214, offset_x_mm = labelX - 160, offset_y_mm = 0 } }
        });
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Contains("R1", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Suggested position", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Geometry", error.GetProperty("metadata").GetProperty("layoutViolations").GetString(), StringComparison.Ordinal);
        PageDrawingGeometry after = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page.Page }, TestContext.Current.CancellationToken);
        Assert.Equal(before, JsonSerializer.Serialize(after));
        await session.UndoAsync();
        PageDrawingGeometry undone = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page.Page }, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(undone.Placements, p => p.Kind == "Text");
        Assert.Contains(undone.Placements, p => p.Kind == "Function");
    }
    [Fact]
    public async Task R1Spike_TwoDifferentFrames_PrintAllCandidateGeometryAndProperties()
    {
        string? variant = Environment.GetEnvironmentVariable("EPLANMCP_DRAWING_VARIANT_BIN");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(variant), "Set EPLANMCP_DRAWING_VARIANT_BIN and prepare LargeWrite with two different frames; close it before probing the copy.");
        string source = Path.Combine(WriteHost.ScratchFolder, WriteHost.WritableName);
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "layout-frame-probe");
        string copy = Path.Combine(folder, WriteHost.WritableName);
        // Read-only copy of the prepared scratch fixture. Probe output stays in the test report.
        File.Copy(source + ".elk", copy + ".elk");
        foreach (string file in Directory.EnumerateFiles(source + ".edb", "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(copy + ".edb", Path.GetRelativePath(source + ".edb", file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var start = new ProcessStartInfo(OfflineHost.RepositoryFile("tests", "EplanMcp.Large.DrawingHelper", "EplanMcp.Large.DrawingHelper.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string value in new[] { variant!, copy + ".elk", "unused", "frame-probe" })
        { start.ArgumentList.Add(value); }
        try
        {
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(); throw; }
            Assert.True(process.ExitCode == 0, await stderr);
            string json = await stdout;
            output.WriteLine(json);
            JsonElement[] pages = JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, pages.Length);
            Assert.NotEqual(pages[0].GetProperty("frame").GetString(), pages[1].GetProperty("frame").GetString());
            Assert.All(pages, p => { Assert.True(p.TryGetProperty("properties", out _)); Assert.True(p.TryGetProperty("framePlacements", out _)); });
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
