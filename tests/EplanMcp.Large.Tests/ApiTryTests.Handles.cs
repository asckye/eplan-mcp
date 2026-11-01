using System.Text.Json;
namespace EplanMcp.Large.Tests;

public sealed partial class ApiTryTests
{
    [Fact]
    public async Task LargeWrite_PinHandleReadAndWriteUseOneUndoStepAndPreserveEarlierHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string[] sentinel = await selection.CreatePagesAsync(1);
        JsonElement connection = await Locator(selection.Project, "Connection");
        JsonElement handle = (await Call(selection.Project, "Connection.StartPin", new { target = connection })).GetProperty("value");
        Assert.Equal("Eplan.EplApi.DataModel.Pin", handle.GetProperty("type").GetString());
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(selection.Project);
        JsonElement read = await Call(selection.Project, "Pin.IntrinsicSafetyPossible", new { target = handle });
        bool original = bool.Parse(read.GetProperty("value").GetString()!);
        Assert.Equal(read.GetProperty("before").GetString(), read.GetProperty("after").GetString());
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(selection.Project).OrderBy(p => p.Key, StringComparer.Ordinal));
        string before = await selection.StateAsync();
        bool replacement = !original;
        JsonElement written = await Call(selection.Project, "Pin.IntrinsicSafetyPossible", new { target = handle, value = replacement });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.Equal(replacement, bool.Parse(written.GetProperty("value").GetString()!));
        Assert.Contains(_approvals.Questions, q => q.Changes.Any(c => c.Contains("Eplan.EplApi.DataModel.Pin returned by O:Eplan.EplApi.DataModel.Connection.StartPin", StringComparison.Ordinal) && c.Contains("unverified", StringComparison.Ordinal)));
        await selection.UndoAsync();
        handle = (await Call(selection.Project, "Connection.StartPin", new { target = await Locator(selection.Project, "Connection") })).GetProperty("value");
        Assert.Equal(original, bool.Parse((await Call(selection.Project, "Pin.IntrinsicSafetyPossible", new { target = handle })).GetProperty("value").GetString()!));
        Assert.Equal(before, await selection.StateAsync());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
    }
}
