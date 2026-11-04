using System.Text.Json;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PageGridWriteTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task CreateOverviewGridFour_ReadsBackAndOneUndoRestoresSnapshot()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(host.Writable));
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        LiveSnapshots.Snapshot before = await selection.SnapshotAsync();
        string page = selection.NextPage;
        var args = new Dictionary<string, object?>
        {
            ["project"] = host.Writable,
            ["pages"] = new[] { new { name = page, page_type = "Overview", grid_size_mm = 4 } },
            ["dry_run"] = false,
        };
        JsonElement? result = null;
        JsonElement? readback = null;
        bool committed = false;
        bool undoAttempted = false;
        try
        {
            result = await host.CallAsync("eplan_page_create", args);
            committed = result.Value.GetProperty("execution").GetString() == "committed";
            Assert.True(committed);
            Assert.Equal("AI: eplan_page_create", result.Value.GetProperty("undo").GetString());
            readback = await host.CallAsync("eplan_page_get", new() { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true });
            Assert.Equal("Overview", readback.Value.GetProperty("page").GetProperty("type").GetString());
            Assert.Equal(4, readback.Value.GetProperty("drawings").GetProperty("gridSize").GetDouble());
            undoAttempted = true;
            await selection.UndoAsync();
            committed = false;
            Assert.Equal(before.Hash, (await selection.SnapshotAsync()).Hash);
        }
        catch (Exception error)
        {
            if (readback is null)
            {
                try
                { readback = await host.CallAsync("eplan_page_get", new() { ["project"] = host.Writable, ["page"] = page, ["include_drawings"] = true }); }
                catch (Exception readError) { TestContext.Current.TestOutputHelper!.WriteLine("readback failure=" + readError); }
            }
            TestContext.Current.TestOutputHelper!.WriteLine("request=" + JsonSerializer.Serialize(args));
            TestContext.Current.TestOutputHelper.WriteLine("result=" + result?.GetRawText());
            TestContext.Current.TestOutputHelper.WriteLine("readback=" + readback?.GetRawText());
            TestContext.Current.TestOutputHelper.WriteLine(error.ToString());
            if (result is null || undoAttempted)
            { selection.PauseCleanup(); }
            throw;
        }
        finally
        {
            if (committed && !undoAttempted)
            { await selection.UndoAsync(); }
        }
    }
}
