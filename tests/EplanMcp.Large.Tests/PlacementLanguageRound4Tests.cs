using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PlacementLanguageRound4Tests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly string[] _englishDisplay = ["en_US"];

    [Fact]
    public async Task TwentyLanguageTextAndGermanWarning_PreserveContents()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession selection = await PlanStepLiveSession.StartAsync(eplan);
        Assert.Equal(host.Writable, selection.Project, ignoreCase: true);
        string page = Assert.Single(await selection.CreatePagesAsync(1));
        string[] codes = ["de_DE", "en_US", "es_ES", "fr_FR", "nl_NL", "sv_SE", "da_DK", "ru_RU", "zh_CN", "pl_PL", "pt_BR", "cs_CZ", "it_IT", "hu_HU", "pt_PT", "ko_KR", "ja_JP", "tr_TR", "ro_RO", "zh_TW"];
        var contents = codes.ToDictionary(code => code, code => code + " " + new string('x', 40), StringComparer.Ordinal);
        var request = Request(page, new { kind = "text", position = (double[])[30, 80], height_mm = 3.5, contents });
        request["layout_guard"] = "warn";
        JsonElement row = Assert.Single((await CreateAndRead(request)).Readback.GetProperty("drawings").GetProperty("items").EnumerateArray());
        string stored = row.GetProperty("properties").GetProperty("text").GetString()!;
        foreach ((string language, string value) in contents)
        { Assert.Contains(language + "@" + value + ";", stored, StringComparison.Ordinal); }
        await selection.UndoAsync();
        Assert.Empty((await Read(page)).GetProperty("drawings").GetProperty("items").EnumerateArray());

        JsonElement languages = await host.CallAsync("eplan_language_set", new() { ["project"] = host.Writable, ["display"] = _englishDisplay, ["dry_run"] = false });
        string[] displayBefore = [.. languages.GetProperty("before").GetProperty("display").EnumerateArray().Select(v => v.GetString()!)];
        try
        {
            request = Request(page, new { kind = "text", position = (double[])[30, 80], height_mm = 3.5, contents = new Dictionary<string, string> { ["de_DE"] = "Eingang" } });
            request["layout_guard"] = "warn";
            (JsonElement result, JsonElement readback) = await CreateAndRead(request);
            Assert.Contains("project display language", result.GetProperty("warning").GetString(), StringComparison.Ordinal);
            row = Assert.Single(readback.GetProperty("drawings").GetProperty("items").EnumerateArray());
            Assert.Equal("de_DE@Eingang;", row.GetProperty("properties").GetProperty("text").GetString());
            await selection.UndoAsync();
            Assert.Empty((await Read(page)).GetProperty("drawings").GetProperty("items").EnumerateArray());
        }
        finally
        { await host.CallAsync("eplan_language_set", new() { ["project"] = host.Writable, ["display"] = displayBefore, ["dry_run"] = false }); }
    }

    private Dictionary<string, object?> Request(string page, params object[] items) => new()
    {
        ["project"] = host.Writable,
        ["page"] = page,
        ["items"] = items,
        ["dry_run"] = false,
        ["snap_to_grid"] = false,
        ["response_format"] = "detailed"
    };

    private Task<JsonElement> Read(string page) => host.CallAsync("eplan_page_get", new()
    {
        ["project"] = host.Writable,
        ["page"] = page,
        ["include_drawings"] = true,
        ["drawing_limit"] = 200,
        ["response_format"] = "detailed"
    });

    private async Task<(JsonElement Result, JsonElement Readback)> CreateAndRead(Dictionary<string, object?> request)
    {
        Evidence("request", request);
        try
        {
            JsonElement result = await host.CallAsync("eplan_placement_create", request);
            Evidence("result", result);
            JsonElement readback = await Read((string)request["page"]!);
            Evidence("readback", readback);
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            return (result, readback);
        }
        catch (Exception ex)
        {
            Evidence("failed result", ex.ToString());
            Evidence("readback after failure", await Read((string)request["page"]!));
            throw;
        }
    }

    private static void Evidence(string label, object value) => TestContext.Current.TestOutputHelper?.WriteLine(label + ": " + JsonSerializer.Serialize(value));
}
