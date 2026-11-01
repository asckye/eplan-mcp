using System.Text.Json;
namespace EplanMcp.Large.Tests;

public sealed partial class ApiTryTests
{
    [Fact]
    public async Task LargeWrite_StaticAndViaReadsAndPageWritePreserveSingleUndoHistory()
    {
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        string sentinel = Assert.Single(await selection.CreatePagesAsync(1));
        JsonElement statik = await Call(selection.Project, "DocumentTypeManager.IsLogicalPageDocType", new { nPageType = "Circuit" });
        Assert.Equal("True", statik.GetProperty("value").GetString());
        Assert.Equal(statik.GetProperty("before").GetString(), statik.GetProperty("after").GetString());
        JsonElement project = await Locator(selection.Project, "Project");
        JsonElement setting = await Call(selection.Project, "Project.DeviceTagSettings.EnableSyntaxCheck", new { target = project, via = "DeviceTagConfig" });
        Assert.True(setting.GetProperty("value").GetString() is "True" or "False");
        Assert.Equal(setting.GetProperty("before").GetString(), setting.GetProperty("after").GetString());
        JsonElement functions = await eplan.CallAsync("eplan_object_find", new() { ["project"] = selection.Project, ["object_type"] = "Function", ["limit"] = 200 });
        JsonElement function = functions.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("locator")).First(l => !string.IsNullOrEmpty(l.GetProperty("page").GetString()));
        string page = function.GetProperty("page").GetString()!;
        string original = (await Call(selection.Project, "Page.GridSize", new { target = function, via = "Page" })).GetProperty("value").GetString()!;
        string before = await selection.StateAsync();
        double changed = double.Parse(original, System.Globalization.CultureInfo.InvariantCulture) == 2.5 ? 4 : 2.5;
        JsonElement written = await Call(selection.Project, "Page.GridSize", new { target = function, via = "Page", value = changed });
        Assert.Equal("committed", written.GetProperty("execution").GetString());
        Assert.Equal(changed.ToString(System.Globalization.CultureInfo.InvariantCulture), (await Call(selection.Project, "Page.GridSize", new { target = await Locator(selection.Project, page: page) })).GetProperty("value").GetString());
        await selection.UndoAsync();
        Assert.Equal(original, (await Call(selection.Project, "Page.GridSize", new { target = await Locator(selection.Project, page: page) })).GetProperty("value").GetString());
        Assert.Equal(before, await selection.StateAsync());
        Assert.Contains((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
        await selection.UndoAsync();
        Assert.DoesNotContain((await selection.SnapshotAsync()).Pages, p => p.Name == sentinel);
    }
}
