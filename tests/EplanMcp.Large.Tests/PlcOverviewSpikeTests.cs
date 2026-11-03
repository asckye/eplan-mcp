using System.Globalization;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/*
实验记录草稿（D8；尚未实机运行，不是 API 结论）：
分别在独立的 Circuit / Overview 页对上，以 Overview、PLCCardOverview 放置已有 PLC 卡和两个 DI 点。
对照 read / place 的每项结果、同名功能的 oid / page / IsMainFunction / symbol，以及 20010、20300、20301、
20472、20473 和属性放置的显示文字；确认新增表示、主辅功能及交叉引用的真实行为。
四页 PNG 和 helper JSON 保留在 %TEMP%\eplanmcp-spikes\D8-*，页面不撤销、不删除；放置结果只作证据，不作断言。
*/
[Trait("Size", "Large")]
[Trait("Spike", "D8")]
public sealed class PlcOverviewSpikeTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task ExistingPlcFunctions_OverviewAndPlcCardOverview_PrintRepresentationsAndExportPages()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        // The fixture is recreated per run; retain every probe page even when a later observation fails.
        session.PauseCleanup();
        JsonElement inputSymbol = await InputSymbolAsync(session.Project, "Up");
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running D8.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            foreach (string placementType in new[] { "Overview", "PLCCardOverview" })
            {
                string circuitPage = session.NextPage, overviewPage = session.NextPage;
                await host.CallAsync("eplan_page_create", new()
                {
                    ["project"] = session.Project,
                    ["pages"] = new[] { new { name = circuitPage, page_type = "Circuit" }, new { name = overviewPage, page_type = "Overview" } },
                });
                string suffix = circuitPage.Split('/')[^1];
                JsonElement card = await host.CallAsync("eplan_device_place", new()
                {
                    ["project"] = session.Project,
                    ["page"] = circuitPage,
                    ["library"] = "SPECIAL",
                    ["symbol"] = "PLCC",
                    ["symbol_variant"] = 0,
                    ["tag"] = "-D8KF" + suffix,
                    ["x"] = 60,
                    ["y"] = 200,
                });
                string cardName = card.GetProperty("device").GetProperty("tag").GetString()!;
                var items = new List<object> { new { name = cardName, symbol = "PLCC", variant = 0, x = 60, y = 200 } };
                for (int i = 0; i < 2; i++)
                {
                    JsonElement point = await host.CallAsync("eplan_device_place", new()
                    {
                        ["project"] = session.Project,
                        ["page"] = circuitPage,
                        ["library"] = inputSymbol.GetProperty("library").GetString(),
                        ["symbol"] = inputSymbol.GetProperty("symbol").GetString(),
                        ["symbol_variant"] = inputSymbol.GetProperty("variant").GetInt32(),
                        ["tag"] = cardName,
                        ["plc_connection_point"] = (i + 2).ToString(CultureInfo.InvariantCulture),
                        ["plc_address"] = "I0." + i.ToString(CultureInfo.InvariantCulture),
                        ["plc_symbolic_address"] = "D8_DI_" + i.ToString(CultureInfo.InvariantCulture),
                        ["plc_text"] = "D8 input " + i.ToString(CultureInfo.InvariantCulture),
                        ["connection_descriptions"] = new[] { "DI a." + i.ToString(CultureInfo.InvariantCulture) },
                        ["x"] = 100 + 40 * i,
                        ["y"] = 160,
                    });
                    items.Add(new { name = point.GetProperty("device").GetProperty("tag").GetString()!, symbol = "PLCCPOV", variant = 0, x = 100 + 40 * i, y = 160 });
                }
                await ProbeAsync(new { project = session.Project, circuitPage, overviewPage, mode = "read", placementType, items }, "D8-" + placementType + "-read-" + suffix);
                await ProbeAsync(new { project = session.Project, circuitPage, overviewPage, mode = "place", placementType, items }, "D8-" + placementType + "-place-" + suffix);
                await ExportAsync(session.Project, circuitPage, "D8-" + placementType + "-circuit-" + suffix);
                await ExportAsync(session.Project, overviewPage, "D8-" + placementType + "-overview-" + suffix);
            }
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
        }
    }

    private async Task<JsonElement> InputSymbolAsync(string project, string direction)
    {
        string? cursor = null;
        do
        {
            JsonElement read = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = project, ["kind"] = "function", ["limit"] = 200, ["cursor"] = cursor });
            foreach (JsonElement symbol in read.GetProperty("symbols").EnumerateArray())
            {
                if (symbol.GetProperty("functionCategory").GetString() == "PLCTerminal" && symbol.TryGetProperty("plcInputDefinition", out JsonElement definition) && definition.ValueKind == JsonValueKind.String
                    && symbol.GetProperty("connectionPoints").GetArrayLength() == 1 && symbol.GetProperty("connectionPoints")[0].GetProperty("direction").GetString() == direction)
                { return JsonSerializer.SerializeToElement(new { library = symbol.GetProperty("library").GetString(), symbol = symbol.GetProperty("name").GetString(), variant = symbol.GetProperty("variant").GetInt32() }); }
            }
            cursor = read.GetProperty("nextCursor").GetString();
            if (read.GetProperty("truncated").GetBoolean() && cursor is null)
            { TestContext.Current.TestOutputHelper!.WriteLine("D8 warning: symbol discovery was truncated without a continuation cursor."); }
        } while (cursor is not null);
        throw new InvalidOperationException("No single-pin native digital-input symbol with direction " + direction + " was discovered.");
    }

    private async Task ProbeAsync(object request, string stage)
    {
        string file = Path.Combine(host.Home, "d8-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargePlcOverviewSpike /REQUEST:\"" + file + "\"");
        Assert.True(File.Exists(file + ".result.json"), "The native D8 probe did not write a response.");
        string text = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine(stage + ": " + text);
        await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), stage + ".json"), text, TestContext.Current.CancellationToken);
    }

    private async Task ExportAsync(string project, string page, string stage)
    {
        JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = project, ["pages"] = new[] { page }, ["format"] = "png", ["width_px"] = 2400 });
        TestContext.Current.TestOutputHelper!.WriteLine(stage + ": " + image.GetRawText());
        string root = image.GetProperty("directory").GetString()!;
        foreach (JsonElement file in image.GetProperty("files").EnumerateArray())
        {
            string target = Path.Combine(EvidenceDirectory(), stage + ".png");
            File.Copy(Path.Combine(root, file.GetProperty("path").GetString()!), target, overwrite: true);
            TestContext.Current.TestOutputHelper!.WriteLine("D8 image: " + target);
        }
    }

    private static string EvidenceDirectory() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
}
