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
    public async Task Create_DisplayAndOtherLanguageOnNewWritePage_PageGetReadsGeometryAndOneUndoRestoresHash()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        WritePlan languages = await bridge.InvokeAsync<LanguageSetArgs, WritePlan>(OperationNames.LanguageSetPlan,
            new() { Project = host.Writable, Add = ["en_US"] }, TestContext.Current.CancellationToken);
        string display = languages.Details![LanguageSetPlanOperation.DisplayBefore].Split('\n').First(LanguageCodes.IsCode);
        string other = display == "en_US" ? "de_DE" : "en_US";
        LiveSnapshots.Snapshot before = await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable });
        string drawingBefore = await DrawingHash(bridge, page);
        var args = new PlacementTextCreateArgs
        {
            Project = host.Writable,
            Page = page,
            Text = new() { Position = [50, 50], HeightMm = 3.5, Contents = new() { [display] = "Visible panel", [other] = "Other language" } },
        };

        // Direct staged Bridge primitive: the Host tool intentionally remains unregistered until T4c.
        WriteResult<PlacementTextCreateData> result = await bridge.InvokeWriteAsync<PlacementTextCreateArgs, PlacementTextCreateData>(
            OperationNames.PlacementTextCreateExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(30), null, TestContext.Current.CancellationToken);
        Assert.False(result.Recovered);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.Warning);
        Assert.Equal(args.Text.Contents, result.Data.Text.Contents);
        Assert.Equal(args.Text.Position, result.Data.Text.Position);
        Assert.Equal(args.Text.HeightMm, result.Data.Text.HeightMm);
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
        Assert.NotEqual(drawingBefore, await DrawingHash(bridge, page));

        await selection.UndoAsync();
        Assert.Equal(drawingBefore, await DrawingHash(bridge, page));
        Assert.Equal(before.Hash, (await LiveSnapshots.TakeAsync(bridge, new() { Project = host.Writable })).Hash);
    }

    [Fact]
    public async Task ForcedReadbackMismatch_OnFreshScratchCopy_RollsBackAndLeavesNoUndoStep()
    {
        string? variantBin = Environment.GetEnvironmentVariable("EPLANMCP_DRAWING_VARIANT_BIN");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(variantBin), "Set EPLANMCP_DRAWING_VARIANT_BIN and build tests/EplanMcp.Large.DrawingHelper; see README.md.");
        string page = (await eplan.CallAsync("eplan_page_list", new() { ["page_type"] = "Circuit" }))
            .GetProperty("pages").EnumerateArray().First().GetProperty("name").GetString()!;
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "text-rollback");
        string copy = Path.Combine(folder, LiveEplan.ProjectName);
        OfflineHost.CopyFixture(LiveEplan.ScratchRoot(), copy);
        string exe = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.DrawingHelper", "EplanMcp.Large.DrawingHelper.exe");
        Assert.True(File.Exists(exe), "Build tests/EplanMcp.Large.DrawingHelper in the configuration of these tests.");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { variantBin!, copy + ".elk", page, "text-rollback" })
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
}
