using System.Text.Json;
namespace EplanMcp.Large.Tests;

public sealed partial class ApiTryTests
{
    [Fact]
    public async Task LargeWrite_SettingsReadsPreserveFilesAndSingleUndoForProjectWrite()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string sentinel = Assert.Single(await selection.CreatePagesAsync(1));
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(selection.Project);
        JsonElement project = await Locator(selection.Project, "Project");
        Assert.Equal("PROJECT", (await Call(selection.Project, "Project.Settings", new { target = project })).GetProperty("value").GetString());
        foreach (string node in new[] { "PROJECT", "USER", "STATION" })
        {
            JsonElement read = await Call(selection.Project, "O:Eplan.EplApi.Base.SettingNode.GetListOfNodes", new { target = project, node, offset = 0, limit = 200 });
            using JsonDocument list = JsonDocument.Parse(read.GetProperty("value").GetString()!);
            Assert.InRange(list.RootElement.GetProperty("items").GetArrayLength(), 0, 200);
            Assert.Equal(read.GetProperty("before").GetString(), read.GetProperty("after").GetString());
        }
        Assert.Equal(files.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(selection.Project).OrderBy(p => p.Key, StringComparer.Ordinal));
        string before = await selection.StateAsync();
        string renamed = selection.NextPage;
        Assert.Equal("committed", (await Call(selection.Project, "Page.Name", new { target = await Locator(selection.Project, page: sentinel), value = renamed })).GetProperty("execution").GetString());
        // Reads after a write must not insert an undo step ahead of that write.
        JsonElement settings = await Call(selection.Project, "Project.DeviceTagSettings.AllowedLetters", new { target = await Locator(selection.Project, "Project") });
        Assert.True(settings.GetProperty("value").GetString() is "True" or "False");
        await selection.UndoAsync();
        Assert.Equal(before, await selection.StateAsync());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
    }
}
