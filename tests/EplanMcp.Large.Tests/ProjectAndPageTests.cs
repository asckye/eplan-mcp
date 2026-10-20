using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_project_*, eplan_page_* against the fixture's known data (api-notes A12). Texts are read with an explicit
/// language, so the results do not depend on EPLAN's user interface language.
/// </summary>
[Trait("Size", "Large")]
public sealed class ProjectAndPageTests(LiveEplan eplan)
{
    private const string PowerSupplyPage = "=GAA+A1&EFS1/1";

    [Fact]
    public async Task ProjectGet_CountsTheFixture()
    {
        JsonElement data = await eplan.CallAsync("eplan_project_get");

        JsonElement counts = data.GetProperty("counts");
        Assert.Equal(LiveEplan.ProjectName, data.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal((321, 504, 2060, 2900), (counts.GetProperty("pages").GetInt32(), counts.GetProperty("devices").GetInt32(), counts.GetProperty("connections").GetInt32(), counts.GetProperty("partReferences").GetInt32()));
        Dictionary<string, int> byType = data.GetProperty("pagesByType").EnumerateArray().ToDictionary(t => t.GetProperty("type").GetString()!, t => t.GetProperty("count").GetInt32());
        Assert.Equal((34, 32, 16), (byType["Circuit"], byType["TerminalDiagram"], byType["PLCDiagram"]));
    }

    [Fact]
    public async Task ProjectGet_Detailed_ReadsPropertiesWithNamesAndIndexes()
    {
        JsonElement properties = (await eplan.CallAsync("eplan_project_get", new() { ["response_format"] = "detailed", ["language"] = "en_US" })).GetProperty("properties");

        JsonElement[] all = [.. properties.EnumerateArray()];
        Assert.InRange(all.Length, 100, 1000);

        // Unverified until now (eplan_project_get spec): property definitions give readable names, and indexed
        // properties come back once per used index.
        Assert.True(all.Count(p => !p.GetProperty("name").GetString()!.All(char.IsAsciiDigit)) > all.Length * 9 / 10);
        Assert.Contains(all, p => p.GetProperty("index").ValueKind == JsonValueKind.Number);
        Assert.All(all, p => Assert.NotEqual(string.Empty, p.GetProperty("value").GetString()));
    }

    [Fact]
    public async Task PageList_FiltersByTypeAndNamePrefix()
    {
        JsonElement all = await eplan.CallAsync("eplan_page_list", new() { ["limit"] = 1 });
        JsonElement terminal = await eplan.CallAsync("eplan_page_list", new() { ["page_type"] = "TerminalDiagram", ["limit"] = 1 });
        JsonElement plc = await eplan.CallAsync("eplan_page_list", new() { ["page_type"] = "PlcDiagram", ["limit"] = 1 });
        JsonElement prefix = await eplan.CallAsync("eplan_page_list", new() { ["name_prefix"] = "=GAA+A1&EFS1/", ["language"] = "zh_CN" });

        Assert.Equal((321, 32, 16), (all.GetProperty("total").GetInt32(), terminal.GetProperty("total").GetInt32(), plc.GetProperty("total").GetInt32()));
        Assert.Equal("PLCDiagram", plc.GetProperty("pages")[0].GetProperty("type").GetString());
        Assert.Equal(
            [(PowerSupplyPage, "400/230 V 电源"), ("=GAA+A1&EFS1/2", "信号灯")],
            prefix.GetProperty("pages").EnumerateArray().Select(p => (p.GetProperty("name").GetString(), p.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task PageList_ReadsEveryPageDescriptionQuickly()
    {
        // The time to read all 321 page descriptions was left for the Large tests to measure (eplan_page_list spec).
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_page_list", new() { ["limit"] = 200, ["language"] = "zh_CN" });
        clock.Stop();

        // The size budget may cut the page short; what counts is that EPLAN read the descriptions of all of them.
        Assert.Equal(321, data.GetProperty("total").GetInt32());
        Assert.NotEqual(0, data.GetProperty("pages").GetArrayLength());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Listing 200 pages took {clock.Elapsed}.");
    }

    [Fact]
    public async Task PageGet_ListsTheDevicesOfThePowerSupplyPage()
    {
        JsonElement data = await eplan.CallAsync("eplan_page_get", new() { ["page"] = PowerSupplyPage, ["language"] = "zh_CN" });

        JsonElement page = data.GetProperty("page");
        Assert.Equal(("Circuit", "400/230 V 电源"), (page.GetProperty("type").GetString(), page.GetProperty("description").GetString()));
        Assert.Equal(12, data.GetProperty("deviceCount").GetInt32());
        Assert.Contains("=GAA+A1-FC1", data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()));
    }

    [Fact]
    public async Task PageGet_UnknownPage_IsNotFound()
    {
        JsonElement error = await eplan.ErrorAsync("eplan_page_get", new() { ["page"] = "=GAA+A1&EFS1/999" });

        Assert.Equal(ErrorReason.PageNotFound, error.GetProperty("reason").GetString());
    }
}
