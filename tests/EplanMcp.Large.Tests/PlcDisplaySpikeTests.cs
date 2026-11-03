using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlcDisplaySpikeTests(LiveEplan eplan)
{
    [Fact]
    [Trait("Spike", "D3")]
    public async Task NativePlcDisplays_NewLargeWritePages_PrintPropertiesPlacementsAndImage()
    {
        (string project, string page, string cardPage, string tag, JsonElement symbol) = await SetupAsync();
        // The two independent points isolate the new plug input from the existing no-plug path.
        (bool baseline, JsonElement baselineResult) = await PlacePointAsync(project, page, tag, symbol, "1", null, false);
        await PrintAsync(project, page, "baseline");
        Assert.True(baseline, baselineResult.GetRawText());
        await TrialPointNameAsync(project, page, tag + ":-X10:1");
        await PrintAsync(project, page, "same point after composite-name trial");
        (bool configured, JsonElement configuredResult) = await PlacePointAsync(project, page, tag, symbol, "2", "-X10", true);
        await PrintAsync(project, page, "configured");
        foreach (string name in new[] { page, cardPage })
        { Print("image", await eplan.CallOrErrorAsync("eplan_export_image", new() { ["project"] = project, ["pages"] = new[] { name }, ["format"] = "png", ["width_px"] = 2400 })); }
        Assert.True(configured, configuredResult.GetRawText());
        // Keep all new pages for inspection; observations are printed, never promoted to API evidence here.
    }

    private async Task TrialPointNameAsync(string project, string page, string name)
    {
        // A55 documents compound point suffixes; this trial observes whether Name also assigns the plug property.
        // It is deliberately separate from the product's explicit 20406 setter, and never asserts that implication.
        JsonElement found = await eplan.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = "PLCTerminal", ["page"] = page, ["limit"] = 200 });
        JsonElement target = Assert.Single(found.GetProperty("items").EnumerateArray()).GetProperty("locator");
        string folder = Directory.CreateTempSubdirectory("eplanmcp-d3-trial-").FullName;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new HostDataLocations(folder));
            services.AddSingleton(new HostConfig([LiveEplan.ScratchRoot()], OutsideScratchWrites.Deny) { ApiTry = ActionRunSwitch.On });
            services.AddEplanMcp();
            services.RemoveAll<IApprovalChannel>();
            services.AddSingleton<IApprovalChannel>(new TrialApprovals(ApiTryTools.Tool));
            await using ServiceProvider provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IBridgeClient>().SelectAsync(eplan.ProcessId, TestContext.Current.CancellationToken);
            ApiTryTools tool = ActivatorUtilities.CreateInstance<ApiTryTools>(provider);
            CallToolResult result = await tool.TryAsync(new Progress<ProgressNotificationValue>(), "O:Eplan.EplApi.DataModel.FunctionBase.Name", project,
                JsonSerializer.SerializeToElement(new { target, value = name }), false, cancellationToken: TestContext.Current.CancellationToken);
            Print("same point card/plug name trial", new { name, result.IsError, result.StructuredContent, result.Content });
            Assert.False(result.IsError == true, JsonSerializer.Serialize(result));
        }
        finally
        { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task PlcChannel_CardTagPlugAndMainReference_AreVisibleAfterCommit()
    {
        (string project, string page, string cardPage, string tag, JsonElement symbol) = await SetupAsync();
        (bool succeeded, JsonElement placed) = await PlacePointAsync(project, page, tag, symbol, "1", "-X10", true);
        await PrintAsync(project, page, "assertion readback");
        Print("image", await eplan.CallOrErrorAsync("eplan_export_image", new() { ["project"] = project, ["pages"] = new[] { page }, ["format"] = "png" }));
        Assert.True(succeeded, placed.GetRawText());
        Assert.Equal(ExecutionState.Committed, placed.GetProperty("execution").GetString());
        JsonElement read = await eplan.CallAsync("eplan_plc_get", new() { ["project"] = project, ["plc"] = tag, ["response_format"] = "detailed", ["language"] = "??_??", ["limit"] = 200 });
        Assert.False(read.GetProperty("truncated").GetBoolean());
        JsonElement channel = Assert.Single(read.GetProperty("channels").EnumerateArray(), c => c.GetProperty("page").GetString() == page && c.GetProperty("address").GetString() == "I0.1");
        Assert.Equal(tag, channel.GetProperty("owner").GetString());
        Assert.Equal("-X10", channel.GetProperty("plugDesignation").GetString());
        // Optional fields are omitted when unset: no 20300 reference means no mainReference field.
        string reference = channel.TryGetProperty("mainReference", out JsonElement main) ? main.GetString() ?? string.Empty : string.Empty;
        Assert.NotEqual(page, cardPage);
        // D3 TRX: a standalone point has no 20300 reference merely because its box is on another page.
        Assert.Equal(string.Empty, reference);
        JsonElement[] drawings = await DrawingsAsync(project, page);
        JsonElement parent = Assert.Single(drawings, r => r.GetProperty("kind").GetString() != "PropertyPlacement" && r.GetProperty("properties").TryGetProperty("name", out JsonElement name) && name.GetString() == placed.GetProperty("device").GetProperty("tag").GetString());
        foreach ((int id, string text, int y) in new[] { (20213, tag[tag.LastIndexOf('-')..], 188), (20300, reference, 184), (20406, "-X10", 180) })
        {
            JsonElement row = Assert.Single(drawings, r => r.GetProperty("kind").GetString() == "PropertyPlacement"
                && r.GetProperty("parent").GetProperty("oid").GetInt64() == parent.GetProperty("locator").GetProperty("oid").GetInt64()
                && r.GetProperty("location")[0].GetDouble() == 124 && r.GetProperty("location")[1].GetDouble() == y);
            string actual = row.GetProperty("properties").GetProperty("display").GetString()!;
            Print("visible property " + id.ToString(CultureInfo.InvariantCulture), actual);
            Assert.Equal("true", row.GetProperty("properties").GetProperty("visible").GetString());
            Assert.Equal(text, actual);
        }
    }

    private async Task<(string Project, string Page, string CardPage, string Tag, JsonElement Symbol)> SetupAsync()
    {
        string project = await eplan.WriteProjectAsync();
        Assert.True(Host.Safety.ScratchPaths.IsInside(project, [WriteHost.ScratchFolder]));
        Assert.Equal(WriteHost.WritableName, Path.GetFileName(project));
        const string Prefix = "=GAA+A1&EFS1/";
        int last = 0;
        string? cursor = null;
        do
        {
            JsonElement pages = await eplan.CallAsync("eplan_page_list", new() { ["project"] = project, ["name_prefix"] = Prefix, ["limit"] = 200, ["cursor"] = cursor });
            foreach (JsonElement row in pages.GetProperty("pages").EnumerateArray())
            {
                if (int.TryParse(row.GetProperty("name").GetString()![Prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                { last = Math.Max(last, number); }
            }
            cursor = pages.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        string page = Prefix + (last + 1).ToString(CultureInfo.InvariantCulture), cardPage = Prefix + (last + 2).ToString(CultureInfo.InvariantCulture);
        await eplan.CallAsync("eplan_page_create", new() { ["dry_run"] = false, ["project"] = project, ["pages"] = new[] { new { name = page }, new { name = cardPage } } });
        await eplan.CallAsync("eplan_page_open", new() { ["project"] = project, ["page"] = page });
        JsonElement card = await eplan.CallAsync("eplan_device_place", new() { ["dry_run"] = false, ["project"] = project, ["page"] = cardPage, ["library"] = "SPECIAL", ["symbol"] = "PLCC", ["symbol_variant"] = 0, ["tag"] = "-D3KF" + (last + 1).ToString(CultureInfo.InvariantCulture), ["x"] = 80, ["y"] = 180 });
        do
        {
            JsonElement symbols = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = project, ["kind"] = "function", ["limit"] = 200, ["cursor"] = cursor });
            foreach (JsonElement symbol in symbols.GetProperty("symbols").EnumerateArray())
            {
                if (symbol.GetProperty("functionCategory").GetString() == "PLCTerminal" && symbol.TryGetProperty("plcInputDefinition", out JsonElement definition) && definition.ValueKind == JsonValueKind.String && symbol.GetProperty("connectionPoints").GetArrayLength() == 1)
                { return (project, page, cardPage, card.GetProperty("device").GetProperty("tag").GetString()!, symbol); }
            }
            cursor = symbols.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        throw new InvalidOperationException("No native one-pin digital-input symbol was discovered.");
    }

    private async Task<(bool Succeeded, JsonElement Result)> PlacePointAsync(string project, string page, string tag, JsonElement symbol, string point, string? plug, bool displays)
    {
        var args = new Dictionary<string, object?> { ["project"] = project, ["page"] = page, ["library"] = symbol.GetProperty("library").GetString(), ["symbol"] = symbol.GetProperty("name").GetString(), ["symbol_variant"] = symbol.GetProperty("variant").GetInt32(), ["tag"] = tag, ["plc_connection_point"] = point, ["plc_address"] = "I0." + point, ["plc_symbolic_address"] = "FICTIONAL_DI_" + point, ["plc_text"] = "Fictional D3 input", ["x"] = point == "1" ? 120 : 180, ["y"] = 176 };
        if (plug is not null)
        { args["plc_plug_designation"] = plug; }
        if (displays)
        { args["display_properties"] = new[] { new { property_id = 20213, offset_x_mm = 4, offset_y_mm = 12 }, new { property_id = 20300, offset_x_mm = 4, offset_y_mm = 8 }, new { property_id = 20406, offset_x_mm = 4, offset_y_mm = 4 }, new { property_id = 20400, offset_x_mm = 4, offset_y_mm = 0 } }; }
        (bool succeeded, JsonElement result) = await eplan.CallOrErrorAsync("eplan_device_place", new(args) { ["dry_run"] = false });
        Print("place", new { args, succeeded, result });
        return (succeeded, result);
    }

    private async Task PrintAsync(string project, string page, string phase)
    {
        string? cursor = null;
        var found = new List<JsonElement>();
        do
        {
            JsonElement objects = await eplan.CallAsync("eplan_object_find", new() { ["project"] = project, ["object_type"] = "PLCTerminal", ["page"] = page, ["limit"] = 200, ["cursor"] = cursor });
            found.AddRange(objects.GetProperty("items").EnumerateArray());
            cursor = objects.GetProperty("cursor").GetString();
        } while (cursor is not null);
        foreach (JsonElement obj in found)
        {
            do
            {
                JsonElement properties = await eplan.CallAsync("eplan_object_get", new() { ["project"] = project, ["object_type"] = "PLCTerminal", ["locator"] = obj.GetProperty("locator"), ["language"] = "??_??", ["response_format"] = "detailed", ["limit"] = 200, ["cursor"] = cursor });
                Print(phase + " properties", properties);
                cursor = properties.GetProperty("cursor").GetString();
            } while (cursor is not null);
        }
        Print(phase + " placements/visible texts", await DrawingsAsync(project, page));
    }

    private async Task<JsonElement[]> DrawingsAsync(string project, string page)
    {
        string? cursor = null;
        var rows = new List<JsonElement>();
        do
        {
            JsonElement drawings = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["drawing_cursor"] = cursor, ["response_format"] = "detailed" })).GetProperty("drawings");
            rows.AddRange(drawings.GetProperty("items").EnumerateArray());
            cursor = drawings.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        return rows.ToArray();
    }

    private static void Print(string label, (bool Succeeded, JsonElement Result) response) => Print(label, new { response.Succeeded, response.Result });

    private static void Print(string label, object value) => TestContext.Current.TestOutputHelper!.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
