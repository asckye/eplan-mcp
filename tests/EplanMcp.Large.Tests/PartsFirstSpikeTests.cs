using System.Text.Json;

namespace EplanMcp.Large.Tests;

/*
实验记录草稿（Parts；尚未实机运行，不是 API 结论）：
1. 保存配置部件库的全部搜索命中，记录 PLC、按钮、端子和电缆的实际选件与缺口。
2. 对照回滚的 templates 输出，观察部件宏、功能模板、类别、符号、引脚和指定属性。
3. 对照 place 的逐功能结果和提交后读回，观察 PLC 点、PLC 盒和按钮的放置行为及页面属性文字。
4. 对照 assign 的部件引用与页面图像；异常也是实验结果，不把预期行为写成断言。
*/
[Trait("Size", "Large")]
[Trait("Spike", "Parts")]
public sealed class PartsFirstSpikeTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private static readonly string[] _queries = ["6ES7521", "6ES7131", "6ES7 521", "3SU1", "3SB3", "3044076", "UT 2,5", "ÖLFLEX CLASSIC 100", "LAPP", "PXC.", "SIE.6ES7"];
    private static readonly string[] _partFields = ["partNumber", "manufacturer", "typeNumber", "orderNumber", "productGroup", "description"];
    private static readonly JsonSerializerOptions _indentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task PartsDatabase_TemplatesPlacementAndAssignment_PrintNativeFacts()
    {
        Assert.Equal(eplan.ProcessId, host.ProcessId);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string evidence = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        List<JsonElement> hits = await SearchAsync(evidence);
        PartPick? plc = Pick("PLC digital input", hits.OrderBy(h => Contains(Text(h, "partNumber"), "6ES7521") ? 0 : 1)
            .Where(h => Contains(Compact(Describe(h)), "6ES7521", "6ES7131")
                || (Contains(Describe(h), "PLC") && Contains(Describe(h), "digital input", "digitaler eingang"))));
        PartPick? pushbutton = Pick("Siemens pushbutton/contact block", hits.Where(h => Contains(Describe(h), "3SU1", "3SB3")
            && Contains(Describe(h), "pushbutton", "push button", "push-button", "contact block", "contact element", "contact module", "Taster", "Kontaktelement", "Kontaktmodul", "Kontaktblock")));
        PartPick? terminal = Pick("Phoenix terminal", hits.Where(h => Contains(Describe(h), "PXC", "Phoenix", "3044076")
            && Contains(Describe(h), "3044076", "UT 2,5", "terminal", "Klemme")));
        PartPick? cable = Pick("Lapp ÖLFLEX CLASSIC 100 cable", hits.Where(h => Contains(Describe(h), "LAPP", "ÖLFLEX", "OLFLEX")
            && Contains(Compact(Describe(h)), "CLASSIC100")));
        PartPick[] picks = new[] { plc, pushbutton, terminal, cable }.OfType<PartPick>().ToArray();

        string page = (await host.NewPagesAsync(1))[0];
        string suffix = page.Split('/')[^1] + Guid.NewGuid().ToString("N")[..6];
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running Parts.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            JsonElement templates = await ProbeAsync(new { project = session.Project, page, mode = "templates", parts = picks.Select(p => new { part = p.Part, variant = p.Variant }) }, "templates", evidence, suffix);
            if (plc is not null)
            {
                JsonElement[] functions = Functions(templates, plc);
                var placements = new List<object>();
                int terminalIndex = 0;
                foreach (JsonElement function in functions.Where(f => Text(f, "category") == "PLCTerminal").Take(2))
                {
                    placements.Add(Placement(function, page, 100 + terminalIndex++ * 40, 100, "IEC_symbol", "PLC_CBOX", 0));
                }

                foreach (JsonElement function in functions.Where(f => Text(f, "category") == "PLCBox").Take(1))
                {
                    placements.Add(Placement(function, page, 60, 60, "SPECIAL", "PLCC", 0));
                }

                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "place",
                    part = plc.Part,
                    variant = plc.Variant,
                    tag = "=GAA+A1-P1KF" + suffix,
                    functionCategory = FirstCategory(functions),
                    placements
                }, "plc-place", evidence, suffix);
            }

            if (pushbutton is not null)
            {
                JsonElement[] functions = Functions(templates, pushbutton);
                object[] placements = functions.Take(2).Select((function, i) =>
                {
                    JsonElement symbol = function.TryGetProperty("symbol", out JsonElement value) ? value : default;
                    int variant = 0;
                    bool reported = symbol.ValueKind == JsonValueKind.Object && Text(symbol, "library").Length > 0 && Text(symbol, "name").Length > 0
                        && symbol.TryGetProperty("variant", out JsonElement symbolVariant) && symbolVariant.ValueKind == JsonValueKind.Number && symbolVariant.TryGetInt32(out variant);
                    return Placement(function, page, 200 + i * 40, 180, reported ? Text(symbol, "library") : "IEC_symbol",
                        reported ? Text(symbol, "name") : "SSD", reported ? variant : 0);
                }).ToArray();
                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "place",
                    part = pushbutton.Part,
                    variant = pushbutton.Variant,
                    tag = "=GAA+A1-P1PB" + suffix,
                    functionCategory = FirstCategory(functions),
                    placements
                }, "pushbutton-place", evidence, suffix);
            }

            var bareArguments = new Dictionary<string, object?>
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = "SSD",
                ["symbol_variant"] = 0,
                ["tag"] = "-P1S" + suffix,
                ["x"] = 280,
                ["y"] = 180,
            };
            if (pushbutton is not null)
            {
                JsonElement first = Functions(templates, pushbutton).FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("pins", out JsonElement pins) && pins.ValueKind == JsonValueKind.Array)
                {
                    string[] pinNames = pins.EnumerateArray().Select(pin => pin.ValueKind == JsonValueKind.Object ? Text(pin, "name") : "").ToArray();
                    if (pinNames.Length > 0 && pinNames.All(name => name.Length > 0))
                    { bareArguments["connection_designations"] = pinNames; }
                    else if (pinNames.Length > 0)
                    { Print("bare device warning", "A template pin name could not be read; connection designations are omitted."); }
                }
                else
                { Print("bare device warning", "The first pushbutton template has no readable pin inventory; connection designations are omitted."); }
            }
            var bare = await eplan.CallOrErrorAsync("eplan_device_place", bareArguments);
            Print("bare device", bare.Result);
            if (pushbutton is not null && bare.Succeeded)
            {
                string name = bare.Result.GetProperty("device").GetProperty("tag").GetString()!;
                await ProbeAsync(new { project = session.Project, page, mode = "assign", name, part = pushbutton.Part, variant = pushbutton.Variant },
                    "assign", evidence, suffix);
            }

            if (plc is not null)
            {
                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "templates-place",
                    part = plc.Part,
                    variant = plc.Variant,
                    x = 60,
                    y = 240,
                    templatePlacements = new[] { new { x = 100, y = 240 }, new { x = 140, y = 240 } }
                }, "templates-place", evidence, suffix);

                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "place-unplaced",
                    part = plc.Part,
                    variant = plc.Variant,
                    x = 60,
                    y = 280,
                    placements = new[] { new { x = 100, y = 280 }, new { x = 140, y = 280 } }
                }, "place-unplaced", evidence, suffix);

                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "device-list",
                    part = plc.Part,
                    variant = plc.Variant,
                    placements = new[] { new { x = 60, y = 320 }, new { x = 100, y = 320 }, new { x = 140, y = 320 } }
                }, "device-list", evidence, suffix);

                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "rename-unplaced",
                    part = plc.Part,
                    variant = plc.Variant,
                    x = 200,
                    y = 60,
                    tag = "=GAA+A1-KN" + suffix,
                    strategy = "name"
                }, "rename-name", evidence, suffix);

                await ProbeAsync(new
                {
                    project = session.Project,
                    page,
                    mode = "rename-unplaced",
                    part = plc.Part,
                    variant = plc.Variant,
                    x = 200,
                    y = 120,
                    tag = "=GAA+A1-KP" + suffix,
                    strategy = "properties"
                }, "rename-properties", evidence, suffix);

                // A82: the "create-named" step (PlaceAt on functions just created by CreateDevice(Project)) crashed EPLAN (0xc0000374); it stays out of this run.
            }

            var exported = await eplan.CallOrErrorAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = new[] { page }, ["format"] = "png" });
            Print("export", exported.Result);
            if (exported.Succeeded)
            {
                foreach (JsonElement file in exported.Result.GetProperty("files").EnumerateArray())
                {
                    string source = Path.Combine(exported.Result.GetProperty("directory").GetString()!, file.GetProperty("path").GetString()!);
                    string target = Path.Combine(evidence, "parts-" + suffix + "-" + Path.GetFileName(source));
                    try
                    {
                        File.Copy(source, target, overwrite: true);
                        Print("image", target);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { Print("image warning", error.ToString()); }
                }
            }
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
        }
    }

    private async Task<List<JsonElement>> SearchAsync(string evidence)
    {
        var hits = new List<JsonElement>();
        var searches = new List<object>();
        foreach (string query in _queries)
        {
            string? cursor = null;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var args = new Dictionary<string, object?> { ["query"] = query, ["limit"] = 100, ["language"] = "en_US" };
                if (cursor is not null)
                { args["cursor"] = cursor; }
                var response = await eplan.CallOrErrorAsync("eplan_part_search", args);
                searches.Add(new { query, cursor, response.Succeeded, result = response.Result });
                if (!response.Succeeded)
                {
                    Print("search warning " + query, response.Result);
                    break;
                }

                foreach (JsonElement hit in response.Result.GetProperty("parts").EnumerateArray())
                {
                    hits.Add(hit.Clone());
                    Print("hit " + query, new
                    {
                        partNumber = Text(hit, "partNumber"),
                        variant = Text(hit, "variant"),
                        description = Text(hit, "description"),
                        hasMacro = hit.GetProperty("hasMacro").GetBoolean()
                    });
                }

                cursor = response.Result.GetProperty("nextCursor").GetString();
                if (cursor is not null && !cursors.Add(cursor))
                {
                    Print("search warning " + query, "The search repeated a cursor; remaining hits are unknown.");
                    break;
                }
            }
            while (cursor is not null);
        }

        string file = Path.Combine(evidence, "parts-search.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { searches, hits }, _indentedJson), TestContext.Current.CancellationToken);
        Print("search evidence", file);
        return hits;
    }

    private static PartPick? Pick(string kind, IEnumerable<JsonElement> candidates)
    {
        JsonElement hit = candidates.FirstOrDefault();
        PartPick? pick = hit.ValueKind == JsonValueKind.Object ? new(kind, Text(hit, "partNumber"), Text(hit, "variant")) : null;
        Print("pick " + kind, (object?)pick ?? "No matching part; this pick is skipped.");
        return pick;
    }

    private static JsonElement[] Functions(JsonElement result, PartPick pick)
    {
        if (result.TryGetProperty("parts", out JsonElement parts))
        {
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (Text(part, "part") == pick.Part && Text(part, "variant") == pick.Variant && part.TryGetProperty("functions", out JsonElement functions))
                { return functions.EnumerateArray().ToArray(); }
            }
        }

        Print("templates warning " + pick.Part, "No returned function inventory; placement will record an empty request.");
        return [];
    }

    private static object Placement(JsonElement function, string page, double x, double y, string library, string symbol, int variant) =>
        new { functionIndex = function.GetProperty("index").GetInt32(), page, x, y, documentType = "Circuit", symbolLibrary = library, symbolName = symbol, symbolVariant = variant };

    private static string? FirstCategory(IEnumerable<JsonElement> functions) => functions.Select(f => Text(f, "category")).FirstOrDefault(category => category.Length > 0);

    private async Task<JsonElement> ProbeAsync(object request, string stage, string evidence, string suffix)
    {
        string file = Path.Combine(host.Home, "parts-" + Guid.NewGuid().ToString("N") + ".json");
        string requestJson = JsonSerializer.Serialize(request);
        await File.WriteAllTextAsync(file, requestJson, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(evidence, "parts-" + suffix + "-" + stage + ".request.json"), requestJson, TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargePartsSpike /REQUEST:\"" + file + "\"");
        Assert.True(File.Exists(file + ".result.json"), "The native Parts helper did not write a response.");
        string text = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        Print(stage, text);
        await File.WriteAllTextAsync(Path.Combine(evidence, "parts-" + suffix + "-" + stage + ".json"), text, TestContext.Current.CancellationToken);
        using JsonDocument result = JsonDocument.Parse(text);
        return result.RootElement.Clone();
    }

    private static string Describe(JsonElement hit) => string.Join(" ", _partFields.Select(field => Text(hit, field)));
    private static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
    private static bool Contains(string text, params string[] values) => values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    private static string Text(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : "";
    private static void Print(string stage, object value) => TestContext.Current.TestOutputHelper!.WriteLine("Parts " + stage + ": " + (value is string text ? text : JsonSerializer.Serialize(value)));
    private sealed record PartPick(string Kind, string Part, string Variant);
}
