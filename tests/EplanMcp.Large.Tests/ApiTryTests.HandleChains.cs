using System.Text.Json;
namespace EplanMcp.Large.Tests;

public sealed partial class ApiTryTests
{
    [Fact]
    public async Task LargeWrite_TwoStepPinConnectionChainReadAndWritePreserveSingleUndoHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string[] sentinel = await selection.CreatePagesAsync(1);
        async Task<JsonElement> ChainedConnection()
        {
            JsonElement pin = (await Call(selection.Project, "Connection.StartPin", new { target = await Locator(selection.Project, "Connection") })).GetProperty("value");
            JsonElement elements = (await Call(selection.Project, "Pin.Connections", new { target = pin })).GetProperty("value").GetProperty("items");
            JsonElement connection = elements.EnumerateArray().First();
            Assert.Equal(JsonValueKind.Number, connection.GetProperty("type").ValueKind);
            Assert.True(connection.TryGetProperty("digest", out _));
            Assert.InRange(elements.GetArrayLength(), 1, 200);
            return connection;
        }
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(selection.Project);
        JsonElement target = await ChainedConnection();
        JsonElement read = await Call(selection.Project, "Connection.WriteProtected", new { target });
        bool original = bool.Parse(read.GetProperty("value").GetString()!);
        Assert.Equal(read.GetProperty("before").GetString(), read.GetProperty("after").GetString());
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(selection.Project).OrderBy(p => p.Key, StringComparer.Ordinal));
        string before = await selection.StateAsync();
        JsonElement written = await Call(selection.Project, "Connection.WriteProtected", new { target, value = !original });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.Equal(!original, bool.Parse(written.GetProperty("value").GetString()!));
        Assert.Contains(_approvals.Questions, q => q.Changes.Any(c => c.Contains("Pin returned by O:Eplan.EplApi.DataModel.Connection.StartPin", StringComparison.Ordinal)));
        await selection.UndoAsync();
        Assert.Equal(original, bool.Parse((await Call(selection.Project, "Connection.WriteProtected", new { target = await ChainedConnection() })).GetProperty("value").GetString()!));
        Assert.Equal(before, await selection.StateAsync());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel[0]);
    }
}
