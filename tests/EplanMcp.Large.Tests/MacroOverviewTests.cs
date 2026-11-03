using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>G2: part group-symbol Overview insertion on LargeWrite, with one-step undo (A33/A79/A82).</summary>
[Trait("Size", "Large")]
public sealed class MacroOverviewTests(LiveEplan eplan)
{
    private const string Part = "PXC.2861344";

    [Fact]
    public async Task PartGroupSymbol_OverviewContainsPlcBoxPointsAndPart_ThenOneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string original = await session.StateAsync();
        string page = session.NextPage;
        var undoStates = new Stack<string>();
        bool outcomeKnown = true;
        async Task<JsonElement> WriteAsync(string tool, Dictionary<string, object?> args)
        {
            string before = await session.StateAsync();
            (bool success, JsonElement result) response;
            try
            {
                response = await eplan.CallOrErrorAsync(tool, args);
            }
            catch
            {
                outcomeKnown = false;
                session.PauseCleanup();
                throw;
            }
            string? execution = response.result.TryGetProperty("execution", out JsonElement state) ? state.GetString() : null;
            if (execution is not ("committed" or "not_started" or "rolled_back"))
            {
                outcomeKnown = false;
                session.PauseCleanup();
            }
            if (execution == "committed")
            {
                undoStates.Push(before);
            }
            Assert.True(response.success, tool + ": " + response.result);
            Assert.Equal("committed", execution);
            return response.result;
        }
        async Task UndoAsync()
        {
            try
            {
                await session.UndoAsync();
                Assert.Equal(undoStates.Peek(), await session.StateAsync());
                undoStates.Pop();
            }
            catch
            {
                outcomeKnown = false;
                session.PauseCleanup();
                throw;
            }
        }
        try
        {
            await WriteAsync("eplan_page_create", new()
            {
                ["project"] = session.Project,
                ["pages"] = new[] { new { name = page, page_type = "Overview" } },
                ["dry_run"] = false,
            });

            // The measured Overview macro is 420 x 276 mm. Use an existing large frame, never relax layout guards.
            bool switchedFrame = await UseLargeFrameAsync(session, bridge, page, WriteAsync);
            string beforeInsert = await session.StateAsync();
            JsonElement result = await WriteAsync("eplan_macro_insert", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["part"] = Part,
                ["part_variant"] = "1",
                ["representation"] = "Overview",
                ["variant"] = 0,
                ["x"] = 20,
                ["y"] = 296,
                ["dry_run"] = false,
            });
            Assert.Equal("AI: eplan_macro_insert", result.GetProperty("undo").GetString());
            Assert.True(result.GetProperty("objectCount").GetInt32() > 0);

            LiveSnapshots.Snapshot snapshot = await session.SnapshotAsync(page);
            Assert.True(snapshot.Chunks[^1].Complete);
            SnapshotObject box = Assert.Single(snapshot.Objects("function"), f => f.Category == "PLCBox" && f.Main == true);
            Assert.Contains(snapshot.Objects("function"), f => f.Category == "PLCTerminal" && f.Page == page);
            JsonElement readPage = await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page });
            Assert.Equal("Overview", readPage.GetProperty("page").GetProperty("type").GetString());
            JsonElement reference = Assert.Single(result.GetProperty("articleReferences").EnumerateArray(), p => p.GetProperty("tag").GetString() == box.Name);
            AssertPart(reference);
            JsonElement device = await eplan.CallAsync("eplan_device_get", new() { ["project"] = session.Project, ["tag"] = box.Name });
            Assert.Equal(page, device.GetProperty("device").GetProperty("page").GetString());
            AssertPart(Assert.Single(device.GetProperty("parts").EnumerateArray(), p => p.GetProperty("partNumber").GetString() == Part));

            await UndoAsync();
            Assert.Equal(beforeInsert, await session.StateAsync());
            Assert.Empty((await session.SnapshotAsync(page)).Objects("function"));
            if (switchedFrame)
            {
                await UndoAsync();
            }
            await UndoAsync();
            Assert.Equal(original, await session.StateAsync());
        }
        finally
        {
            // Undo confirmed writes even after failed assertions or a missing-frame skip; never retry an uncertain undo.
            if (outcomeKnown)
            {
                while (undoStates.Count > 0)
                {
                    await UndoAsync();
                }
                Assert.Equal(original, await session.StateAsync());
            }
        }
    }

    private static async Task<bool> UseLargeFrameAsync(PlanStepLiveSession session, BridgeClient bridge, string page,
        Func<string, Dictionary<string, object?>, Task<JsonElement>> writeAsync)
    {
        async Task<PageDrawingGeometry> Geometry(string name) => await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(
            OperationNames.PageDrawingGet, new() { Project = session.Project, Page = name }, TestContext.Current.CancellationToken);
        static bool Fits(PageDrawingGeometry geometry) => geometry.PageSize is { Length: 2 } size && size[0] >= 460 && size[1] >= 316;

        if (Fits(await Geometry(page)))
        {
            return false;
        }
        string[] pages = [.. (await session.SnapshotAsync()).Pages.Select(p => p.Name)];
        foreach (string[] batch in pages.Chunk(50))
        {
            Dictionary<string, PagePlotFrameState> frames = await bridge.InvokeAsync<ProjectSnapshotArgs, Dictionary<string, PagePlotFrameState>>(
                OperationNames.PagePlotFrameInspect, new() { Project = session.Project, Pages = [.. batch] }, TestContext.Current.CancellationToken);
            foreach ((string name, PagePlotFrameState frame) in frames)
            {
                if (frame.Effective is null || !Fits(await Geometry(name)))
                {
                    continue;
                }
                await writeAsync("eplan_page_update", new()
                {
                    ["project"] = session.Project,
                    ["pages"] = new[] { new { name = page, plot_frame = frame.Effective } },
                    ["dry_run"] = false,
                });
                Assert.True(Fits(await Geometry(page)), "The selected existing frame must provide room for the measured Overview macro.");
                return true;
            }
        }
        Assert.Skip("Prepare an existing large plot frame in LargeWrite (at least 460 x 316 mm) for the 420 x 276 mm PXC.2861344 Overview macro; layout guards remain enabled.");
        return false;
    }

    private static void AssertPart(JsonElement reference)
    {
        Assert.Equal(Part, reference.GetProperty("partNumber").GetString());
        Assert.Equal("1", reference.GetProperty("variant").GetString());
        Assert.Equal(1, reference.GetProperty("count").GetInt32());
    }
}
