using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementTextCreateTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task Create_MixedBatchOnNewWritePage_PageGetReadsGeometryAndOneUndoRestoresHash()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        WritePlan languages = await bridge.InvokeAsync<LanguageSetArgs, WritePlan>(OperationNames.LanguageSetPlan,
            new() { Project = host.Writable, Add = ["en_US"] }, TestContext.Current.CancellationToken);
        // The copy may leave the display languages unset; then only a language-independent text is visible (A54).
        string display = languages.Details![LanguageSetPlanOperation.DisplayBefore].Split('\n').FirstOrDefault(LanguageCodes.IsCode) ?? "??_??";
        string other = display == "en_US" ? "de_DE" : "en_US";
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
        string drawingBefore = await DrawingHash(bridge, page);
        var args = new PlacementTextCreateArgs
        {
            Project = host.Writable,
            Page = page,
            Text = new() { Position = [50, 50], HeightMm = 3.5, Contents = display == "??_??" ? new() { [display] = "Visible panel" } : new() { [display] = "Visible panel", [other] = "Other language" } },
        };

        JsonElement result = await host.CallAsync("eplan_placement_create", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["snap_to_grid"] = false,
            ["items"] = new object[]
            {
                new { kind = "text", position = args.Text.Position, contents = args.Text.Contents, height_mm = args.Text.HeightMm },
                new { kind = "line", points = (double[][])[[10, 20], [30, 40]] },
                new { kind = "rectangle", corners = (double[][])[[60, 80], [20, 30]] },
            },
        });
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.False(result.GetProperty("recovered").GetBoolean());
        Assert.Equal(3, result.GetProperty("items").GetArrayLength());
        JsonElement discovery = await host.CallAsync("eplan_page_get", new()
        {
            ["project"] = host.Writable,
            ["page"] = page,
            ["include_drawings"] = true,
            ["drawing_limit"] = 200,
            ["response_format"] = "detailed",
        });
        JsonElement drawings = discovery.GetProperty("drawings");
        Assert.False(drawings.GetProperty("truncated").GetBoolean());
        JsonElement row = Assert.Single(drawings.GetProperty("items").EnumerateArray(), r => r.GetProperty("kind").GetString() == "Text");
        Assert.Equal(args.Text.Position, row.GetProperty("location").EnumerateArray().Select(n => n.GetDouble()));
        JsonElement properties = row.GetProperty("properties");
        Assert.Equal(args.Text.HeightMm, double.Parse(properties.GetProperty("height_mm").GetString()!, CultureInfo.InvariantCulture));
        string stored = properties.GetProperty("text").GetString()!;
        Assert.Equal(args.Text.Contents.Count, stored.Split(';', StringSplitOptions.RemoveEmptyEntries).Length);
        foreach ((string language, string text) in args.Text.Contents)
        { Assert.Contains(language + "@" + text + ";", stored, StringComparison.Ordinal); }
        JsonElement line = Assert.Single(drawings.GetProperty("items").EnumerateArray(), r => r.GetProperty("kind").GetString() == "Line");
        Assert.Equal([10d, 20d], line.GetProperty("location").EnumerateArray().Select(n => n.GetDouble()));
        AssertOutline([10d, 20d, 30d, 40d], line.GetProperty("bounds")); // live T4c: bounds include the pen width
        JsonElement rectangle = Assert.Single(drawings.GetProperty("items").EnumerateArray(), r => r.GetProperty("kind").GetString() == "Rectangle");
        AssertOutline([20d, 30d, 60d, 80d], rectangle.GetProperty("bounds"));
        Assert.NotEqual(drawingBefore, await DrawingHash(bridge, page));

        await selection.UndoAsync();
        Assert.Equal(drawingBefore, await DrawingHash(bridge, page));
        Assert.Equal(before.Hash, (await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable })).Hash);
    }

    [Fact]
    public async Task ForcedMidBatchFailure_OnFreshScratchCopy_RollsBackAndLeavesNoUndoStep()
    {
        string? variantBin = Environment.GetEnvironmentVariable("EPLANMCP_DRAWING_VARIANT_BIN");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(variantBin), "Set EPLANMCP_DRAWING_VARIANT_BIN and build tests/EplanMcp.Large.DrawingHelper; see README.md.");
        string page = (await eplan.CallAsync("eplan_page_list", new() { ["page_type"] = "Circuit" }))
            .GetProperty("pages").EnumerateArray().First().GetProperty("name").GetString()!;
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "mixed-rollback");
        string copy = Path.Combine(folder, LiveEplan.ProjectName);
        OfflineHost.CopyFixture(LiveEplan.ScratchRoot(), copy);
        string exe = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.DrawingHelper", "EplanMcp.Large.DrawingHelper.exe");
        Assert.True(File.Exists(exe), "Build tests/EplanMcp.Large.DrawingHelper in the configuration of these tests.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { variantBin!, copy + ".elk", page, "mixed-rollback" })
        { start.ArgumentList.Add(argument); }
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        JsonElement proof = JsonDocument.Parse(await stdout).RootElement;
        Assert.Equal(ExecutionState.RolledBack, proof.GetProperty("execution").GetString());
        Assert.Equal(proof.GetProperty("before").GetString(), proof.GetProperty("after").GetString());
        Assert.Equal(proof.GetProperty("original").GetString(), proof.GetProperty("afterUndo").GetString());
        Assert.True(proof.GetProperty("sentinelRemoved").GetBoolean());
        Directory.Delete(folder, recursive: true);
    }

    private async Task<string> DrawingHash(BridgeClient bridge, string page) => SnapshotCanonical.Sha256(JsonSerializer.Serialize(
        await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = host.Writable, Page = page }, TestContext.Current.CancellationToken)));

    /// <summary>The drawn geometry inside the reported bounds, which add at most 1 mm of pen width on each side.</summary>
    private static void AssertOutline(double[] geometry, JsonElement bounds)
    {
        double[] actual = [.. bounds.EnumerateArray().Select(n => n.GetDouble())];
        Assert.InRange(geometry[0] - actual[0], 0, 1);
        Assert.InRange(geometry[1] - actual[1], 0, 1);
        Assert.InRange(actual[2] - geometry[2], 0, 1);
        Assert.InRange(actual[3] - geometry[3], 0, 1);
    }
}
