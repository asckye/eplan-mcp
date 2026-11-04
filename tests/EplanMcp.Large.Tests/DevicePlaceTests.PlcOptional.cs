using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task PlcPoint_WithOnlyDesignationAndText_ReadsBackWithoutAddresses()
    {
        string project = await eplan.WriteProjectAsync();
        string page = await NewPageAsync(project);
        string tag = "-KFO" + page[Prefix.Length..];
        try
        {
            JsonElement card = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project, ["page"] = page, ["library"] = "SPECIAL", ["symbol"] = "PLCC",
                ["symbol_variant"] = 0, ["tag"] = tag, ["x"] = 40, ["y"] = 100, ["dry_run"] = false
            });
            tag = card.GetProperty("device").GetProperty("tag").GetString()!;
            JsonElement? input = null;
            string? cursor = null;
            do
            {
                JsonElement found = await PartsCallAsync("eplan_symbol_search", new()
                { ["project"] = project, ["kind"] = "function", ["limit"] = 200, ["cursor"] = cursor });
                input = found.GetProperty("symbols").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(s =>
                    s!.Value.GetProperty("functionCategory").GetString() == "PLCTerminal"
                    && s.Value.TryGetProperty("plcInputDefinition", out JsonElement definition) && definition.ValueKind == JsonValueKind.String
                    && s.Value.GetProperty("connectionPoints").GetArrayLength() == 1);
                cursor = found.GetProperty("nextCursor").GetString();
            } while (input is null && cursor is not null);
            JsonElement symbol = Assert.NotNull(input);
            JsonElement placed = await PartsCallAsync("eplan_device_place", new()
            {
                ["project"] = project, ["page"] = page, ["tag"] = tag,
                ["library"] = symbol.GetProperty("library").GetString(), ["symbol"] = symbol.GetProperty("name").GetString(),
                ["symbol_variant"] = symbol.GetProperty("variant").GetInt32(), ["plc_connection_point"] = "2",
                ["plc_text"] = "Start input", ["x"] = 120, ["y"] = 180, ["dry_run"] = false
            });
            Assert.Equal(ExecutionState.Committed, placed.GetProperty("execution").GetString());
            JsonElement read = await PartsCallAsync("eplan_plc_get", new()
            { ["project"] = project, ["plc"] = tag, ["language"] = "??_??", ["response_format"] = "detailed" });
            JsonElement channel = Assert.Single(read.GetProperty("channels").EnumerateArray(), c => c.GetProperty("name").GetString()!.EndsWith(":2", StringComparison.Ordinal));
            Assert.Equal("Start input", channel.GetProperty("text").GetString());
            Assert.True(string.IsNullOrEmpty(channel.GetProperty("address").GetString()));
            Assert.True(string.IsNullOrEmpty(channel.GetProperty("manualSymbolicAddress").GetString()));
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            await PartsCallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true });
            throw;
        }
    }
}
