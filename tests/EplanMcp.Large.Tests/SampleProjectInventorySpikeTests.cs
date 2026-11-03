using System.Text.Json;

namespace EplanMcp.Large.Tests;

/*
实验记录草稿（2026-11-02，只读；尚未实机运行）：
负责人要求盘点 EPLAN 示例项目都有什么，以便列出完整绘制一个项目还缺哪些工具。本探测只调用读工具：
项目概况、全部页（按页类型统计）、全部主功能（按功能类别统计）、端子排、部件汇总，
并对每种页类型抽前两页读取图形对象（按对象种类与显示属性统计）。结果写到 %TEMP%\eplanmcp-spikes\sample-inventory.json。
*/
[Trait("Size", "Large")]
[Trait("Spike", "Inventory")]
public sealed class SampleProjectInventorySpikeTests(LiveEplan eplan)
{
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };
    [Fact]
    public async Task SampleProject_PagesDevicesAndDrawingKinds_WritesTheInventory()
    {
        var output = TestContext.Current.TestOutputHelper!;
        async Task<List<JsonElement>> All(string tool, string key, Dictionary<string, object?> args)
        {
            var rows = new List<JsonElement>();
            string? cursor = null;
            do
            {
                var call = new Dictionary<string, object?>(args) { ["limit"] = 200, ["cursor"] = cursor };
                JsonElement result = await eplan.CallAsync(tool, call);
                rows.AddRange(result.GetProperty(key).EnumerateArray().Select(r => r.Clone()));
                cursor = result.TryGetProperty("nextCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
            } while (cursor is not null);
            return rows;
        }
        static string Field(JsonElement row, params string[] names)
        {
            foreach (string name in names)
            {
                if (row.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                { return value.GetString()!; }
            }
            return "?";
        }
        JsonElement project = await eplan.CallAsync("eplan_project_get", new() { ["response_format"] = "detailed" });
        List<JsonElement> pages = await All("eplan_page_list", "pages", []);
        List<JsonElement> devices = await All("eplan_device_list", "devices", []);
        List<JsonElement> auxiliary = await All("eplan_device_list", "devices", new() { ["include_auxiliary"] = true });
        JsonElement strips = await eplan.CallAsync("eplan_terminal_strip_list", new() { ["limit"] = 200 });
        JsonElement parts = await eplan.CallAsync("eplan_part_list", new() { ["limit"] = 200 });
        var pageTypes = pages.GroupBy(p => Field(p, "type", "pageType")).ToDictionary(g => g.Key, g => g.Select(p => Field(p, "name")).ToArray());
        var categories = devices.GroupBy(d => Field(d, "category")).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
        var auxiliaryCategories = auxiliary.GroupBy(d => Field(d, "category")).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
        var drawings = new Dictionary<string, object>();
        foreach ((string type, string[] names) in pageTypes)
        {
            foreach (string name in names.Take(2))
            {
                var items = new List<JsonElement>();
                string? cursor = null;
                do
                {
                    var args = new Dictionary<string, object?> { ["page"] = name, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" };
                    if (cursor is not null)
                    { args["drawing_cursor"] = cursor; }
                    JsonElement data = (await eplan.CallAsync("eplan_page_get", args)).GetProperty("drawings");
                    items.AddRange(data.GetProperty("items").EnumerateArray().Select(r => r.Clone()));
                    cursor = data.TryGetProperty("nextCursor", out JsonElement next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
                } while (cursor is not null && items.Count < 2000);
                drawings[type + " | " + name] = new
                {
                    kinds = items.GroupBy(i => Field(i, "kind")).ToDictionary(g => g.Key, g => g.Count()),
                    displayedProperties = items.Where(i => Field(i, "kind") == "PropertyPlacement" && i.TryGetProperty("properties", out _))
                        .GroupBy(i => Field(i.GetProperty("properties"), "property_id")).ToDictionary(g => g.Key, g => g.Count()),
                    symbols = items.Where(i => i.TryGetProperty("properties", out JsonElement p) && p.TryGetProperty("symbol", out _))
                        .GroupBy(i => Field(i.GetProperty("properties"), "symbol")).ToDictionary(g => g.Key, g => g.Count()),
                };
                output.WriteLine($"Inventory {type} {name}: {items.Count} drawing items");
            }
        }
        string evidence = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        string file = Path.Combine(evidence, "sample-inventory.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
        {
            project,
            pageTypes = pageTypes.ToDictionary(p => p.Key, p => new { count = p.Value.Length, pages = p.Value }),
            pageRows = pages,
            mainFunctionCategories = categories,
            allFunctionCategories = auxiliaryCategories,
            strips,
            parts,
            drawings,
        }, _indented), TestContext.Current.CancellationToken);
        output.WriteLine("Inventory: " + file);
        output.WriteLine("Page types: " + JsonSerializer.Serialize(pageTypes.ToDictionary(p => p.Key, p => p.Value.Length)));
        output.WriteLine("Main function categories: " + JsonSerializer.Serialize(categories));
    }
}
