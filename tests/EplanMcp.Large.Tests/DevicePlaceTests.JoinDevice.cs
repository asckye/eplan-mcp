using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task JoinDevice_SecondSymbolHasTheExistingTagAndIsNotMain()
    {
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        string tag = "-KJ" + page[Prefix.Length..];
        try
        {
            JsonElement first = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = "K",
                ["symbol_variant"] = 0,
                ["tag"] = tag,
                ["x"] = 80,
                ["y"] = 160,
                ["dry_run"] = false
            });
            tag = first.GetProperty("device").GetProperty("tag").GetString()!;
            JsonElement original = (await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag })).GetProperty("device");
            JsonElement joined = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = "K",
                ["symbol_variant"] = 0,
                ["tag"] = tag,
                ["x"] = 160,
                ["y"] = 160,
                ["join_device"] = true,
                ["dry_run"] = false
            });
            Assert.Equal(ExecutionState.Committed, joined.GetProperty("execution").GetString());
            Assert.Equal(tag, joined.GetProperty("device").GetProperty("tag").GetString());
            Assert.False(joined.GetProperty("device").GetProperty("isMainFunction").GetBoolean());
            JsonElement read = await PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });
            Assert.Equal(original.GetRawText(), read.GetProperty("device").GetRawText());
            JsonElement list = await PartsCallAsync("eplan_device_list", new()
            { ["project"] = project, ["page"] = page, ["tag_contains"] = tag, ["include_auxiliary"] = true, ["limit"] = 200 });
            JsonElement[] representations = [.. list.GetProperty("devices").EnumerateArray()];
            Assert.Equal(2, representations.Length);
            Assert.All(representations, representation => Assert.Equal(tag, representation.GetProperty("tag").GetString()));
            Assert.Single(representations, representation => representation.GetProperty("main").GetBoolean());
            Assert.Single(representations, representation => !representation.GetProperty("main").GetBoolean());
            JsonElement drawing = await LiveDrawings.ReadAllAsync(PartsCallAsync, project, page);
            JsonElement[] symbols = [.. drawing.EnumerateArray()
                .Where(item => item.GetProperty("kind").GetString() == "Function")];
            Assert.Equal(2, symbols.Length);
            Assert.All(symbols, symbol => Assert.Equal(tag, symbol.GetProperty("properties").GetProperty("name").GetString()));
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            await PartsCallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true });
            throw;
        }
    }
}
