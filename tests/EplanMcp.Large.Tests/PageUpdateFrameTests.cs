using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PageUpdateFrameTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task FrameSwitch_DryRunIndependentReadbackOtherPagesAndOneCurrentProjectUndo_RestoreOriginal()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        Assert.StartsWith(WriteHost.ScratchFolder + Path.DirectorySeparatorChar, host.Writable, StringComparison.OrdinalIgnoreCase);
        // A41/A46: closes only known fixture copies, checks current before undo, never reopens LargeWrite.
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(host.ProcessId);
        LiveSnapshots.Snapshot snapshot = await selection.SnapshotAsync();
        string[] pages = [.. snapshot.Pages.Select(p => p.Name)];
        Dictionary<string, PagePlotFrameState> before = await ReadAsync(bridge, pages);
        string[] frames = [.. before.Values.Select(s => s.Effective).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        Assert.True(frames.Length >= 2, "Prepare at least two different plot frames used by pages in LargeWrite; the test never imports master data.");
        KeyValuePair<string, PagePlotFrameState> page = before.Where(p => p.Value.Effective is not null)
            .OrderBy(p => p.Value.Setting is null ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal).First();
        string target = frames.First(f => f != page.Value.Effective);
        string baseline = State(before);
        int audit = host.AuditRecords().Count;
        JsonElement preview = await host.CallAsync("eplan_page_update", Args(page.Key, target, dryRun: true));
        Assert.Equal("not_started", preview.GetProperty("execution").GetString());
        JsonElement planned = Assert.Single(preview.GetProperty("pages").EnumerateArray());
        Assert.Equal((page.Value.Effective, target), (planned.GetProperty("previousPlotFrame").GetString(), planned.GetProperty("plotFrame").GetString()));
        Assert.Equal(baseline, State(await ReadAsync(bridge, pages)));
        Assert.Equal(audit, host.AuditRecords().Count);

        bool committed = false;
        bool undoAttempted = false;
        bool undoReturned = false;
        try
        {
            JsonElement result = await host.CallAsync("eplan_page_update", Args(page.Key, target));
            committed = result.GetProperty("execution").GetString() == "committed";
            Assert.True(committed);
            Assert.Equal("AI: eplan_page_update", result.GetProperty("undo").GetString());
            Assert.False(result.GetProperty("recovered").GetBoolean());
            Dictionary<string, PagePlotFrameState> after = await ReadAsync(bridge, pages);
            AssertOnlyFrameChanged(before, after, page.Key, target);
            Assert.Equal(OtherPages(snapshot, page.Key), OtherPages(await selection.SnapshotAsync(), page.Key));

            // A repeated request is a no-op, including no transaction/empty undo step or audit.
            audit = host.AuditRecords().Count;
            JsonElement unchanged = await host.CallAsync("eplan_page_update", Args(page.Key, target));
            Assert.Equal(("not_started", 1, 0), (unchanged.GetProperty("execution").GetString(), unchanged.GetProperty("unchanged").GetInt32(), unchanged.GetProperty("pages").GetArrayLength()));
            Assert.Equal(audit, host.AuditRecords().Count);
            undoAttempted = true;
            await selection.UndoAsync();
            undoReturned = true;
            Assert.Equal(baseline, State(await ReadAsync(bridge, pages)));
            Assert.Equal(snapshot.Hash, (await selection.SnapshotAsync()).Hash);
            committed = false;
            TestContext.Current.TestOutputHelper!.WriteLine($"11016 before={page.Value.Setting ?? "<empty>"}; effective {page.Value.Effective} -> {target}; one current-project undo restored both values and all other pages.");
        }
        finally
        {
            // No retry/compensation for an unknown commit or undo outcome. A confirmed remaining change must still
            // match exactly the test's scope before restoration; empty inherited settings require native undo.
            if (committed && (!undoAttempted || undoReturned))
            {
                Dictionary<string, PagePlotFrameState> current = await ReadAsync(bridge, pages);
                if (State(current) != baseline)
                {
                    AssertOnlyFrameChanged(before, current, page.Key, target);
                    if (!undoAttempted)
                    {
                        await selection.UndoAsync();
                    }
                    else
                    {
                        JsonElement restored = await host.CallAsync("eplan_page_update", Args(page.Key, page.Value.Effective!));
                        Assert.Equal("committed", restored.GetProperty("execution").GetString());
                    }
                }

                PagePlotFrameState restoredState = (await ReadAsync(bridge, pages))[page.Key];
                Assert.Equal(page.Value.Effective, restoredState.Effective);
                Assert.Equal(page.Value.Setting, restoredState.Setting);
            }
        }
    }

    private Dictionary<string, object?> Args(string page, string frame, bool dryRun = false) => new()
    {
        ["project"] = host.Writable,
        ["pages"] = new[] { new { name = page, plot_frame = frame } },
        ["dry_run"] = dryRun,
    };

    private async Task<Dictionary<string, PagePlotFrameState>> ReadAsync(BridgeClient bridge, string[] pages)
    {
        var result = new Dictionary<string, PagePlotFrameState>(StringComparer.Ordinal);
        foreach (string[] chunk in pages.Chunk(50))
        {
            Dictionary<string, PagePlotFrameState> batch = await bridge.InvokeAsync<ProjectSnapshotArgs, Dictionary<string, PagePlotFrameState>>(
                OperationNames.PagePlotFrameInspect, new() { Project = host.Writable, Pages = [.. chunk] }, TestContext.Current.CancellationToken);
            foreach ((string name, PagePlotFrameState state) in batch)
            {
                result.Add(name, state);
            }
        }

        Assert.Equal(pages.Order(StringComparer.Ordinal), result.Keys.Order(StringComparer.Ordinal));
        return result;
    }

    private static void AssertOnlyFrameChanged(Dictionary<string, PagePlotFrameState> before, Dictionary<string, PagePlotFrameState> after, string targetPage, string frame)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach ((string name, PagePlotFrameState state) in before)
        {
            Assert.Equal(state.Identifier, after[name].Identifier);
            Assert.Equal(name == targetPage ? frame : state.Setting, after[name].Setting);
            Assert.Equal(name == targetPage ? frame : state.Effective, after[name].Effective);
        }
    }

    private static string State(Dictionary<string, PagePlotFrameState> pages) => JsonSerializer.Serialize(pages.OrderBy(p => p.Key, StringComparer.Ordinal));
    private static string OtherPages(LiveSnapshots.Snapshot snapshot, string target) => JsonSerializer.Serialize(snapshot.Pages.Where(p => p.Name != target));
}
