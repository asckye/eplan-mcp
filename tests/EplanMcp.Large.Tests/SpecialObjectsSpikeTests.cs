using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Large.Tests;

/// <summary>Records G4–G8 tool and native observations on one new scratch page without asserting API outcomes.</summary>
[Trait("Size", "Large")]
[Trait("Spike", "SpecialObjects")]
public sealed class SpecialObjectsSpikeTests(LiveEplan eplan, DestructiveHost host) : IClassFixture<DestructiveHost>
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly JsonArray _stages = [];
    private readonly JsonArray _warnings = [];
    private JsonObject _evidence = new();
    private string _evidenceFile = "";
    private bool _registered;

    [Fact]
    public async Task SpecialObjects_ExistingToolsAndNativeCreation_PrintCommittedReadback()
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
        _evidenceFile = Path.Combine(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName,
            "special-objects-" + stamp + ".json");
        _evidence = new JsonObject { ["startedUtc"] = stamp, ["stages"] = _stages, ["warnings"] = _warnings };
        try
        {
            Assert.Equal(eplan.ProcessId, host.ProcessId);
            await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
            session.PauseCleanup(); // Retain the page for the maintainer's visual inspection, including failed trials.
            string project = session.Project;
            string page = (await host.NewPagesAsync(1))[0];
            _evidence["project"] = project;
            _evidence["page"] = page;
            await SaveAsync();

            string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
            if (File.Exists(helper))
            {
                try
                {
                    await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
                    _registered = true;
                }
                catch (Exception error) { Warn("Helper registration failed: " + error); }
            }
            else
            { Warn("Build tests/EplanMcp.Large.ConnectionHelper before the live run; native observations are unavailable."); }

            Dictionary<string, object?> Place(string library, string symbol, int variant, double x, double y) => new()
            {
                ["project"] = project,
                ["page"] = page,
                ["dry_run"] = false,
                ["library"] = library,
                ["symbol"] = symbol,
                ["symbol_variant"] = variant,
                ["x"] = x,
                ["y"] = y,
            };
            async Task Trial(string label, Dictionary<string, object?> arguments)
            {
                JsonElement result = await CallAsync(label, "eplan_device_place", arguments);
                Print(label + ": " + (Succeeded(result) ? "tool succeeded" : "tool failed; complete error retained"));
                await ReadPageAsync(session, page, label);
            }

            await Trial("SPECIAL/DCP A", new(Place("SPECIAL", "DCP", 0, 40, 240)) { ["tag"] = "-PF9:1" });
            await Trial("SPECIAL/BCP A", new(Place("SPECIAL", "BCP", 0, 100, 240)) { ["tag"] = "-WE9" });
            (string Library, string Symbol, int Variant)[] terminals =
            [
                ("IEC_symbol", "X2_2", 0), ("IEC_symbol", "XTR1", 1),
                ("IEC_symbol", "XFD", 0), ("GRAPHICS", "KA1_PE", 0),
            ];
            for (int i = 0; i < terminals.Length; i++)
            {
                (string library, string symbol, int variant) = terminals[i];
                await Trial(library + "/" + symbol, new(Place(library, symbol, variant, 160 + 60 * i, 240))
                {
                    ["terminal_strip"] = "-XG8" + page.Split('/')[^1],
                    ["terminal_number"] = (i + 1).ToString(CultureInfo.InvariantCulture),
                });
            }

            string plcTag = "-KG8" + page.Split('/')[^1];
            JsonElement card = await CallAsync("PLC parent setup", "eplan_device_place",
                new(Place("SPECIAL", "PLCC", 0, 280, 60)) { ["tag"] = plcTag });
            await ReadPageAsync(session, page, "PLC parent setup");
            if (Succeeded(card) && Response(card).TryGetProperty("device", out JsonElement device)
                && device.TryGetProperty("tag", out JsonElement tag) && tag.ValueKind == JsonValueKind.String)
            { plcTag = tag.GetString()!; }
            else
            { Warn("PLC parent setup did not return a tag; the requested point is still attempted with its explicit parent tag."); }
            await Trial("IEC_symbol/PLC_CBOX_PCON_PLUG A", new(Place("IEC_symbol", "PLC_CBOX_PCON_PLUG", 0, 280, 100))
            {
                ["tag"] = plcTag,
                ["plc_connection_point"] = "1",
                ["plc_address"] = "I0.0",
                ["plc_symbolic_address"] = "SPECIAL_OBJECTS_DI",
                ["plc_text"] = "Special objects spike input",
            });

            double[]? midpoint = await PrepareConnectionAsync(session, page);
            if (_registered)
            {
                JsonElement native = await NativeAsync(new
                {
                    project,
                    page,
                    mode = "probe",
                    connectionReady = midpoint is not null,
                    x = midpoint?[0],
                    y = midpoint?[1],
                    blackBoxTag = "=GAA+A1-PF9N" + page.Split('/')[^1],
                    busbarTag = "=GAA+A1-WE9N" + page.Split('/')[^1],
                }, "native probe");
                if (native.ValueKind == JsonValueKind.Object && native.TryGetProperty("conclusions", out JsonElement conclusions))
                {
                    foreach (JsonElement conclusion in conclusions.EnumerateArray())
                    { Print(conclusion.ValueKind == JsonValueKind.String ? conclusion.GetString()! : conclusion.GetRawText()); }
                }
                await ReadPageAsync(session, page, "native final");
            }
        }
        catch (Exception error)
        {
            _evidence["error"] = error.ToString();
            throw;
        }
        finally
        {
            if (_registered)
            {
                try
                { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
                catch (Exception error) { Warn("Helper unregister failed: " + error); }
            }
            await SaveAsync();
            Print("SpecialObjects evidence: " + _evidenceFile);
        }
    }

    private async Task<double[]?> PrepareConnectionAsync(PlanStepLiveSession session, string page)
    {
        string strip = "-XG4" + page.Split('/')[^1];
        var placed = new List<JsonElement>();
        foreach ((double x, double y, string number) in new[] { (40d, 60d, "1"), (100d, 140d, "2") })
        {
            JsonElement result = await CallAsync("wire terminal " + number, "eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["dry_run"] = false,
                ["terminal_strip"] = strip,
                ["terminal_number"] = number,
                ["symbol_variant"] = 0,
                ["x"] = x,
                ["y"] = y,
            });
            placed.Add(result);
            await ReadPageAsync(session, page, "wire terminal " + number);
        }
        try
        {
            if (placed.Any(p => !Succeeded(p)))
            { Warn("The CDP/PDP wire requires both setup terminals; see their complete tool results."); return null; }
            JsonElement? symbol = await FindTerminalSymbolAsync(session.Project);
            if (symbol is null)
            { Warn("IEC_symbol/X A pin evidence is unavailable; CDP/PDP on-line creation is unverified."); return null; }
            JsonElement drawing = await CallAsync("wire endpoint locators", "eplan_page_get", PageArguments(session.Project, page));
            if (!Succeeded(drawing))
            { Warn("Wire endpoint locators are unavailable."); return null; }
            JsonElement[] rows = [.. Response(drawing).GetProperty("drawings").GetProperty("items").EnumerateArray()];
            (object Reference, double[] Position) Endpoint(JsonElement placement, string direction)
            {
                JsonElement location = Response(placement).GetProperty("terminal").GetProperty("position");
                JsonElement row = rows.Single(r => Text(r, "kind") != "PropertyPlacement"
                    && r.GetProperty("location")[0].GetDouble() == location[0].GetDouble()
                    && r.GetProperty("location")[1].GetDouble() == location[1].GetDouble());
                var pins = symbol.Value.GetProperty("connectionPoints").EnumerateArray().Select((pin, index) => (pin, index));
                (JsonElement pin, int index) = pins.Single(p => Text(p.pin, "direction") == direction);
                double[] position = [location[0].GetDouble() + pin.GetProperty("location")[0].GetDouble(),
                    location[1].GetDouble() + pin.GetProperty("location")[1].GetDouble()];
                return (new { locator = row.GetProperty("locator"), pin_index = index }, position);
            }
            var from = Endpoint(placed[0], "Up");
            var to = Endpoint(placed[1], "Down");
            double middleY = (from.Position[1] + to.Position[1]) / 2;
            double[][] points = [from.Position, [from.Position[0], middleY], [to.Position[0], middleY], to.Position];
            JsonElement wire = await CallAsync("explicit CDP wire", "eplan_connection_create", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["dry_run"] = false,
                ["response_format"] = "detailed",
                ["items"] = new[] { new { kind = "wire", from = from.Reference, to = to.Reference, points } },
            });
            await ReadPageAsync(session, page, "explicit CDP wire");
            if (!Succeeded(wire))
            { Warn("Explicit wire creation failed; CDP/PDP on-line trials will report unavailable setup."); return null; }
            // Read back the middle segment, rather than assuming that requested coordinates survived snapping.
            JsonElement segment = Response(wire).GetProperty("graphicalAutoconnect").EnumerateArray().Single(s =>
                s[0][1].GetDouble() == s[1][1].GetDouble()
                && Math.Min(s[0][0].GetDouble(), s[1][0].GetDouble()) == Math.Min(from.Position[0], to.Position[0])
                && Math.Max(s[0][0].GetDouble(), s[1][0].GetDouble()) == Math.Max(from.Position[0], to.Position[0]));
            double[] midpoint = [(segment[0][0].GetDouble() + segment[1][0].GetDouble()) / 2, segment[0][1].GetDouble()];
            _evidence["wireMidpoint"] = JsonSerializer.SerializeToNode(midpoint);
            return midpoint;
        }
        catch (Exception error)
        { Warn("Wire setup/readback failed: " + error); return null; }
    }

    private async Task<JsonElement?> FindTerminalSymbolAsync(string project)
    {
        string? cursor = null;
        do
        {
            JsonElement result = await CallAsync("wire terminal symbol", "eplan_symbol_search", new()
            {
                ["project"] = project,
                ["library"] = "IEC_symbol",
                ["name_contains"] = "X",
                ["limit"] = 200,
                ["cursor"] = cursor,
            });
            if (!Succeeded(result))
            { return null; }
            JsonElement response = Response(result);
            foreach (JsonElement symbol in response.GetProperty("symbols").EnumerateArray())
            {
                if (Text(symbol, "name") == "X" && symbol.GetProperty("variant").GetInt32() == 0)
                { return symbol; }
            }
            cursor = Text(response, "nextCursor");
        } while (cursor is not null);
        return null;
    }

    private async Task ReadPageAsync(PlanStepLiveSession session, string page, string stage)
    {
        await CallAsync(stage + " page readback", "eplan_page_get", PageArguments(session.Project, page));
        try
        {
            LiveSnapshots.Snapshot snapshot = await session.SnapshotAsync(page);
            _stages.Add(new JsonObject { ["stage"] = stage + " snapshot", ["response"] = JsonSerializer.SerializeToNode(snapshot.Chunks, _json) });
        }
        catch (Exception error) { Warn(stage + " snapshot failed: " + error); }
        if (_registered)
        { await NativeAsync(new { project = session.Project, page, mode = "read" }, stage + " native readback"); }
        await SaveAsync();
    }

    private async Task<JsonElement> CallAsync(string stage, string tool, Dictionary<string, object?> arguments)
    {
        var row = new JsonObject { ["stage"] = stage, ["tool"] = tool, ["arguments"] = JsonSerializer.SerializeToNode(arguments) };
        _stages.Add(row);
        try
        {
            (bool succeeded, JsonElement result) = await eplan.CallOrErrorAsync(tool, arguments);
            row["succeeded"] = succeeded;
            row["response"] = JsonSerializer.SerializeToNode(result);
        }
        catch (Exception error)
        { row["succeeded"] = false; row["exception"] = error.ToString(); }
        await SaveAsync();
        return JsonSerializer.SerializeToElement(row);
    }

    private async Task<JsonElement> NativeAsync(object request, string stage)
    {
        string file = Path.Combine(host.Home, "special-objects-" + Guid.NewGuid().ToString("N") + ".json");
        var row = new JsonObject { ["stage"] = stage, ["request"] = JsonSerializer.SerializeToNode(request) };
        _stages.Add(row);
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeSpecialObjectsSpike /REQUEST:\"" + file + "\"");
            string response = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
            row["response"] = JsonNode.Parse(response);
            await SaveAsync();
            using JsonDocument document = JsonDocument.Parse(response);
            return document.RootElement.Clone();
        }
        catch (Exception error)
        {
            row["exception"] = error.ToString();
            Warn(stage + " failed; complete exception retained.");
            await SaveAsync();
            return default;
        }
    }

    private static Dictionary<string, object?> PageArguments(string project, string page) => new()
    {
        ["project"] = project,
        ["page"] = page,
        ["include_drawings"] = true,
        ["drawing_limit"] = 200,
        ["response_format"] = "detailed",
    };

    private static bool Succeeded(JsonElement row) => row.GetProperty("succeeded").GetBoolean();
    private static JsonElement Response(JsonElement row) => row.GetProperty("response");
    private static string? Text(JsonElement value, string key) => value.TryGetProperty(key, out JsonElement item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private Task SaveAsync() => File.WriteAllTextAsync(_evidenceFile, _evidence.ToJsonString(_json), TestContext.Current.CancellationToken);
    private void Warn(string message) { _warnings.Add(message); Print("SpecialObjects warning: " + message); }
    private static void Print(string message) => TestContext.Current.TestOutputHelper!.WriteLine(message);
}
