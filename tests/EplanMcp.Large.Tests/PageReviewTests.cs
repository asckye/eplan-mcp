using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class PageReviewTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData("message", "eplan_message")]
    [InlineData("dangling", "dangling_end")]
    [InlineData("text", "overlap")]
    [InlineData("text", "off_grid")]
    [InlineData("text", "expected_mismatched")]
    [InlineData("open_pin", "unconnected_pin")]
    [InlineData("networks", "unintended_junction")]
    [InlineData("pairing", "interruption_pairing")]
    [InlineData("cable", "cable_core_segment")]
    public async Task NewLargeWritePage_ReportsDeliberateIssue_ExistingToolsRepair_ReviewIsClean(string scenario, string code)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(1);
        string page = pages[0];
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build the connection helper before running page review Large tests.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            await NativeAsync(new { project = session.Project, pages, mode = "review_setup", scenario });
            await Generate();
            JsonElement drawings = await Drawing();
            double grid = drawings.GetProperty("gridSize").GetDouble();
            JsonObject expected = new();
            (double[][] Trunk, double[][] Branch) cableLayout = ([], []);
            string check = code switch { "overlap" => "overlaps", "dangling_end" => "dangling_ends", "eplan_message" => "eplan_messages", "unconnected_pin" => "unconnected_pins", "unintended_junction" => "unintended_junctions", "cable_core_segment" => "cable_cores", "expected_mismatched" => "expected", _ => code };
            JsonElement[] functions = drawings.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("kind").GetString() == "Function").OrderBy(d => d.GetProperty("properties").GetProperty("name").GetString(), StringComparer.Ordinal).ToArray();
            // connection_list returns null for unnamed pins; review expects a string, including "" (A55).
            JsonNode End(JsonElement connection, string end) => new JsonObject { ["device"] = connection.GetProperty(end).GetProperty("device").GetString(), ["pin"] = connection.GetProperty(end).GetProperty("pin").GetString() ?? "" };
            JsonArray Connections(JsonElement connections, (int From, int To)[] pairs) => new(pairs.Select(p => (JsonNode?)new JsonObject { ["from"] = End(connections[p.From], "from"), ["to"] = End(connections[p.To], "to") }).ToArray());
            if (code == "expected_mismatched")
            { expected["texts"] = new JsonArray(Enumerable.Range(0, 2).Select(i => (JsonNode?)new JsonObject { ["text"] = "Review fixture", ["position"] = new JsonArray((20 + 20 * i) * grid, 40 * grid) }).ToArray()); }
            if (scenario == "networks")
            {
                JsonElement connections = (await ReadConnections()).GetProperty("connections");
                Assert.Equal(2, connections.GetArrayLength());
                expected["connections"] = Connections(connections, [(0, 1), (1, 0)]);
            }
            JsonElement[] points = drawings.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("kind").GetString() == "InterruptionPoint").OrderBy(d => d.GetProperty("location")[0].GetDouble()).ToArray();
            if (scenario == "pairing")
            {
                expected["interruptions"] = new JsonArray(new JsonObject { ["name"] = points[0].GetProperty("properties").GetProperty("name").GetString(), ["position"] = JsonNode.Parse(points[0].GetProperty("location").GetRawText()), ["counterpart"] = new JsonObject { ["page"] = page, ["position"] = JsonNode.Parse(points[2].GetProperty("location").GetRawText()) } });
            }
            if (scenario == "cable")
            {
                string a = functions[0].GetProperty("properties").GetProperty("name").GetString()!;
                string b = functions[1].GetProperty("properties").GetProperty("name").GetString()!;
                JsonElement geometry = await NativeAsync(new { project = session.Project, pages, mode = "read" });
                cableLayout = PageReviewCableLayout.Read(geometry.GetProperty("observation"), a, b, grid);
                string cable = "=GAA+A1-WReview" + page.Split('/')[^1];
                JsonElement defined = await NativeAsync(new { project = session.Project, pages, mode = "define", cable, line = cableLayout.Trunk });
                JsonElement observed = Assert.Single(defined.GetProperty("observation").GetProperty("cables").EnumerateArray());
                string actualCable = observed.GetProperty("name").GetString()!;
                Assert.True(Bridge.Core.Operations.CableDefinitionPlanner.SameName(cable, actualCable, page), defined.GetRawText());
                cable = actualCable;
                await Generate();
                await NativeAsync(new { project = session.Project, pages, mode = "bind", cable });
                JsonElement bound = await NativeAsync(new { project = session.Project, pages, mode = "review_cores" });
                Assert.Contains(bound.GetProperty("observation").GetProperty("connections").EnumerateArray()
                    .SelectMany(c => c.GetProperty("definitionPoints").EnumerateArray()).GroupBy(p => p.GetProperty("oid").GetInt64()), g => g.Count() > 1);
                // Keep the A-B target fixed; choose geometry from the observed routes, independently of finder order.
                JsonElement connection = Assert.Single((await ReadConnections()).GetProperty("connections").EnumerateArray(), c =>
                    c.GetProperty("from").GetProperty("device").GetString() == a && c.GetProperty("to").GetProperty("device").GetString() == b
                    || c.GetProperty("from").GetProperty("device").GetString() == b && c.GetProperty("to").GetProperty("device").GetString() == a);
                expected["cables"] = new JsonArray(new JsonObject { ["designation"] = cable, ["cores"] = new JsonArray(new JsonObject { ["from"] = End(connection, "from"), ["to"] = End(connection, "to"), ["label"] = "1", ["colour"] = "RD" }) });
            }
            JsonElement before = await Review();
            TestContext.Current.TestOutputHelper!.WriteLine("Review before repair: " + before.GetRawText());
            Assert.Contains(before.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("code").GetString() == code);
            if (scenario == "cable")
            {
                // A73 (live 2026-11-02): moving the cable onto the branch drops its core assignment and generation does not
                // restore it. No existing tool reassigns cores, so this scenario stops at the verified detection and target.
                JsonElement issue = Assert.Single(before.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == code);
                Assert.Equal("eplan_connection_create", issue.GetProperty("fix").GetProperty("tool").GetString());
                JsonElement cablePlacement = Assert.Single((await Drawing()).GetProperty("items").EnumerateArray(), p => p.GetProperty("kind").GetString() == "Cable");
                double[] location = cablePlacement.GetProperty("location").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                Assert.Equal([location[0] + cableLayout.Branch[0][0] - cableLayout.Trunk[0][0], location[1] + cableLayout.Branch[0][1] - cableLayout.Trunk[0][1]],
                    issue.GetProperty("position").EnumerateArray().Select(v => v.GetDouble()));
                Assert.Skip("Cable core reassignment after a move has no tool yet (api-notes A73); detection and target position are verified.");
            }
            switch (scenario)
            {
                case "message":
                    await host.CallAsync("eplan_check_run", new() { ["project"] = session.Project, ["pages"] = pages });
                    break;
                case "dangling":
                    JsonElement available = await host.CallAsync("eplan_symbol_search", new() { ["project"] = session.Project, ["library"] = "IEC_symbol", ["name_contains"] = "BAT", ["limit"] = 200 });
                    foreach ((double x, string direction) in new[] { (30 * grid, "Right"), (50 * grid, "Left") })
                    {
                        JsonElement candidate = available.GetProperty("symbols").EnumerateArray().First(s => s.GetProperty("name").GetString() == "BAT" && s.GetProperty("connectionPoints").EnumerateArray().Any(p => p.GetProperty("direction").GetString() == direction));
                        JsonElement pin = candidate.GetProperty("connectionPoints").EnumerateArray().First(p => p.GetProperty("direction").GetString() == direction);
                        await host.CallAsync("eplan_device_place", new() { ["dry_run"] = false, ["project"] = session.Project, ["page"] = page, ["library"] = "IEC_symbol", ["symbol"] = "BAT", ["symbol_variant"] = candidate.GetProperty("variant").GetInt32(), ["tag"] = "-R" + page.Split('/')[^1] + direction, ["x"] = x - pin.GetProperty("location")[0].GetDouble(), ["y"] = 40 * grid - pin.GetProperty("location")[1].GetDouble() });
                    }
                    break;
                case "text":
                    JsonElement[] texts = drawings.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("kind").GetString() == "Text").ToArray();
                    await Move(texts.Select((t, i) => (t, (20 + 20 * i) * grid, 40 * grid)).ToArray());
                    break;
                case "networks":
                    double[] b = functions[1].GetProperty("location").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    double[] d = functions[3].GetProperty("location").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    await Move([(functions[1], d[0], d[1]), (functions[3], b[0], b[1])]);
                    await Generate();
                    break;
                case "pairing":
                    await Move([(points[1], 100 * grid, 40 * grid)]);
                    await Generate();
                    break;
                case "open_pin":
                    JsonElement symbol = await host.CallAsync("eplan_symbol_search", new() { ["project"] = session.Project, ["library"] = "IEC_symbol", ["name_contains"] = "BAT", ["limit"] = 200 });
                    JsonElement variant = Assert.Single(symbol.GetProperty("symbols").EnumerateArray(), s => s.GetProperty("name").GetString() == "BAT" && s.GetProperty("variant").GetInt32() == before.GetProperty("issues")[0].GetProperty("readback")[0].GetProperty("symbol_variant").GetInt32());
                    double[] origin = functions[0].GetProperty("location").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    var items = variant.GetProperty("connectionPoints").EnumerateArray().Select(pin =>
                    {
                        string direction = pin.GetProperty("direction").GetString()!;
                        double x = origin[0] + pin.GetProperty("location")[0].GetDouble(), y = origin[1] + pin.GetProperty("location")[1].GetDouble();
                        return new { kind = "potential_point", position = new[] { x + (direction == "Right" ? 8 * grid : direction == "Left" ? -8 * grid : 0), y + (direction == "Up" ? 8 * grid : direction == "Down" ? -8 * grid : 0) }, directions = new[] { direction switch { "Right" => "Left", "Left" => "Right", "Up" => "Down", _ => "Up" } } };
                    }).ToArray();
                    await host.CallAsync("eplan_connection_create", new() { ["dry_run"] = false, ["project"] = session.Project, ["page"] = page, ["items"] = items });
                    break;
            }
            JsonElement after = await Review();
            TestContext.Current.TestOutputHelper!.WriteLine("Review after repair: " + after.GetRawText());
            if (scenario == "dangling")
            { await NativeAsync(new { project = session.Project, pages, mode = "read" }); }
            Assert.True(after.GetProperty("summary").GetProperty("clean").GetBoolean(), after.GetRawText());
            Assert.Empty(after.GetProperty("issues").EnumerateArray());

            async Task Generate() => await host.CallAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = pages });
            async Task<JsonElement> Drawing() => (await host.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" })).GetProperty("drawings");
            async Task<JsonElement> ReadConnections() => await host.CallAsync("eplan_connection_list", new() { ["project"] = session.Project, ["page"] = page, ["response_format"] = "detailed", ["limit"] = 200 });
            async Task<JsonElement> Review() => await host.CallAsync("eplan_page_review", new() { ["project"] = session.Project, ["page"] = page, ["checks"] = new[] { check }, ["expected"] = expected, ["response_format"] = "detailed" });
            async Task Move((JsonElement Row, double X, double Y)[] moves) => await host.CallAsync("eplan_placement_update", new() { ["dry_run"] = false, ["project"] = session.Project, ["page"] = page, ["moves"] = moves.Select(m => new { locator = m.Row.GetProperty("locator"), x_mm = m.X, y_mm = m.Y }).ToArray() });
        }
        finally { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }
    private async Task<JsonElement> NativeAsync(object request)
    {
        string file = Path.Combine(host.Home, "review-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSpike /REQUEST:\"" + file + "\"");
        using JsonDocument response = JsonDocument.Parse(await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken));
        TestContext.Current.TestOutputHelper!.WriteLine("Native review fixture: " + response.RootElement.GetRawText());
        Assert.True(response.RootElement.GetProperty("success").GetBoolean(), response.RootElement.GetRawText());
        return response.RootElement.Clone();
    }
}
