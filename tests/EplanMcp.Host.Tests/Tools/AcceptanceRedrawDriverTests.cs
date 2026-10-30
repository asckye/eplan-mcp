using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class AcceptanceRedrawDriverTests
{
    [Fact]
    public async Task MissingToolContracts_ReportEverySkippedItem_LeavePagesAndCountConfirmedUndos()
    {
        var fake = new DrawingTools();
        var driver = Driver(fake);
        await driver.DrawAsync();
        await driver.ExportAsync();
        Assert.Equal(["eplan_page_create", "eplan_page_open", "eplan_page_get"], fake.Calls.Take(3));
        Assert.Equal("eplan_export_image", fake.Calls[^1]);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("undo", StringComparison.OrdinalIgnoreCase) || c.Contains("delete", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(10, driver.UndoSteps);
        Assert.False(driver.UndoCountIncomplete);
        JsonElement report = JsonSerializer.SerializeToElement(driver.Report);
        Assert.Equal(["pages", "location_boxes", "devices", "plc", "terminals", "wires_and_symbols", "generate", "cables", "interruption_points", "texts", "verify"], report.GetProperty("stages").EnumerateArray().Select(n => n.GetString()));
        string[] skipped = report.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("item").GetString()!).ToArray();
        Assert.Contains("plc", skipped);
        Assert.Contains("strip", skipped);
        Assert.All(Enumerable.Range(0, 7), i => Assert.Contains("input" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), skipped));
        Assert.All(Enumerable.Range(1, 8), i => Assert.Contains("terminal" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), skipped));
        Assert.Contains("cableW3", skipped);
        Assert.Contains("cableW4", skipped);
        Assert.Contains("link1/page", skipped);
        Assert.Contains("link1/counterpart", skipped);
        Assert.All(report.GetProperty("skipped").EnumerateArray(), item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("reason").GetString())));
    }

    [Theory]
    [InlineData(ExecutionState.Unknown, true)]
    [InlineData(ExecutionState.RolledBack, false)]
    [InlineData(ExecutionState.NotStarted, false)]
    public async Task UnexpectedError_StopsWithoutRetryOrUndo_AndPreservesEvidence(string state, bool incomplete)
    {
        var fake = new DrawingTools { FailPlace = state };
        var driver = Driver(fake);
        await Assert.ThrowsAsync<InvalidOperationException>(driver.DrawAsync);
        Assert.Equal(1, fake.Calls.Count(c => c == "eplan_device_place"));
        Assert.DoesNotContain("eplan_connection_generate", fake.Calls);
        Assert.Equal(1, driver.UndoSteps);
        Assert.Equal(incomplete, driver.UndoCountIncomplete);
    }

    [Fact]
    public async Task ExplicitPath_ResolvesFreshPageLocatorsAndActualDesignations_AndCountsTwoUndoSteps()
    {
        var fake = new DrawingTools { ConnectWires = true, PaginateDrawings = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", TwoSwitches(), "test", _ => { });
        await driver.DrawAsync();
        JsonElement wire = Assert.Single(fake.ConnectionItems!.Value.EnumerateArray());
        Assert.Equal((1L, 2L), (wire.GetProperty("from").GetProperty("locator").GetProperty("oid").GetInt64(), wire.GetProperty("to").GetProperty("locator").GetProperty("oid").GetInt64()));
        Assert.Equal(1, wire.GetProperty("from").GetProperty("pin_index").GetInt32());
        Assert.Equal(1, wire.GetProperty("to").GetProperty("pin_index").GetInt32());
        Assert.Equal([48d, 204d], wire.GetProperty("points")[0].EnumerateArray().Select(n => n.GetDouble()));
        Assert.Equal([80d, 204d], wire.GetProperty("points")[3].EnumerateArray().Select(n => n.GetDouble()));
        Assert.True(fake.ContinuationReads > 0);
        Assert.Equal(8, driver.UndoSteps);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Readback_WrongDesignationOrDuplicateEdge_FailsEvenWithOtherSkippedItems(bool wrongPin, bool duplicate)
    {
        var fake = new DrawingTools { ConnectWires = true, WrongPin = wrongPin, DuplicateEdge = duplicate };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", TwoSwitches(), "test", _ => { });
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
    }

    [Fact]
    public async Task OneUnavailableEndpoint_SkipsTheWholeSharedWireBatch_WithoutClaimingMissingEdgesWereDrawn()
    {
        JsonObject list = JsonNode.Parse(TwoSwitches().GetRawText())!.AsObject();
        list["wires"]!.AsArray().Add(JsonNode.Parse("""{"id":"blocked","from":{"item":"terminal1","pin":"2"},"to":{"item":"input0","pin":"1"},"points":["from","to"]}"""));
        var fake = new DrawingTools { ConnectWires = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(list.ToJsonString()), "test", _ => { });
        await driver.DrawAsync();
        Assert.Null(fake.ConnectionItems);
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
        Assert.Equal(6, driver.UndoSteps);
        Assert.True(driver.SkippedCount > 0);
    }

    private static JsonElement TwoSwitches()
    {
        JsonObject list = JsonNode.Parse(AcceptanceDrawingListTests.Fixture())!.AsObject();
        list["devices"] = new JsonArray(list["devices"]![0]!.DeepClone(), list["devices"]![1]!.DeepClone());
        list["wires"] = JsonNode.Parse("""[{"id":"testwire","from":{"item":"switch1","pin":"14"},"to":{"item":"switch2","pin":"14"},"points":["from",[48,188],[80,188],"to"]}]""");
        list["expected_connections"] = JsonNode.Parse("""[{"from":{"item":"switch1","pin":"14"},"to":{"item":"switch2","pin":"14"}}]""");
        list["nodes"] = new JsonArray();
        list["cables"] = new JsonArray();
        list["interruption_points"] = new JsonArray();
        return AcceptanceDrawingList.Read(list.ToJsonString());
    }

    private static AcceptanceRedrawDriver Driver(DrawingTools fake) => new(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(AcceptanceDrawingListTests.Fixture()), "test", _ => { });

    private sealed class DrawingTools
    {
        internal List<string> Calls { get; } = [];
        internal string? FailPlace { get; init; }
        internal bool ConnectWires { get; init; }
        internal bool PaginateDrawings { get; init; }
        internal bool WrongPin { get; init; }
        internal bool DuplicateEdge { get; init; }
        internal JsonElement? ConnectionItems { get; private set; }
        internal int ContinuationReads { get; private set; }
        private readonly List<object> _drawings = [];
        private readonly Dictionary<string, string[]> _pins = new(StringComparer.Ordinal);
        private long _oid;

        internal Task<(bool Succeeded, JsonElement Result)> CallAsync(string tool, Dictionary<string, object?> args)
        {
            Calls.Add(tool);
            Assert.Equal("LargeWrite", args["project"]);
            object result;
            if (tool == "eplan_device_place" && FailPlace is { } state)
            { return Task.FromResult((false, JsonSerializer.SerializeToElement(new { reason = ErrorReason.EplanOperationFailed, execution = state }))); }
            switch (tool)
            {
                case "eplan_page_create":
                    result = new { execution = "committed", undo = "Create pages" };
                    break;
                case "eplan_page_open":
                    result = new { editor_opened = true };
                    break;
                case "eplan_device_place":
                    if (args.TryGetValue("location_box", out object? structure))
                    {
                        result = new { execution = "committed", undo = "Location box", locationBox = new { structure, outline = "dashed", bounds = new[] { args["x"], args["y"], args["corner_x"], args["corner_y"] } } };
                    }
                    else
                    {
                        string tag = (string)args["tag"]!;
                        string[] pins = ((JsonElement)args["connection_designations"]!).EnumerateArray().Select(n => n.GetString()!).ToArray();
                        _pins[tag] = pins;
                        _drawings.Add(new { kind = "Function", properties = new { name = tag }, location = new[] { args["x"], args["y"] }, locator = new { project = "LargeWrite", page = args["page"], type = 1, oid = ++_oid, digest = new string('a', 64) } });
                        result = new { execution = "committed", undo = "Device", recovered = false, device = new { tag, category = "NoContact" }, symbol = new { connectionPoints = pins.Select((_, i) => new { location = new[] { 0, -8 * i }, direction = i == 0 ? "Up" : "Down" }) } };
                    }
                    break;
                case "eplan_device_get":
                    result = new { device = new { pins = _pins[(string)args["tag"]!] }, parts = Array.Empty<object>(), truncated = false };
                    break;
                case "eplan_page_get":
                    int offset = args.TryGetValue("drawing_cursor", out object? cursor) ? int.Parse((string)cursor!, System.Globalization.CultureInfo.InvariantCulture) : 0;
                    if (cursor is not null)
                    { ContinuationReads++; }
                    int count = PaginateDrawings ? 1 : _drawings.Count;
                    string? next = offset + count < _drawings.Count ? (offset + count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                    result = new { drawings = new { gridSize = 4, items = _drawings.Skip(offset).Take(count), nextCursor = next, truncated = next is not null } };
                    break;
                case "eplan_connection_generate":
                    result = new { execution = "not_started", undo = (string?)null };
                    break;
                case "eplan_connection_create":
                    if (!ConnectWires)
                    { return Task.FromResult((false, JsonSerializer.SerializeToElement(new { reason = ErrorReason.CapabilityMissing, execution = "not_started", message = "No new electrical connection to generate for an isolated interruption point." }))); }
                    ConnectionItems = JsonSerializer.SerializeToElement(args["items"]);
                    result = new { execution = "committed", undoSteps = 2 };
                    break;
                case "eplan_placement_create":
                    foreach (JsonElement item in JsonSerializer.SerializeToElement(args["items"]).EnumerateArray())
                    { _drawings.Add(new { kind = "Text", properties = new { text = "??_??@" + item.GetProperty("contents").GetProperty("??_??").GetString() + ";" }, location = item.GetProperty("position") }); }
                    result = new { execution = "committed" };
                    break;
                case "eplan_connection_list":
                    string[] tags = _pins.Keys.ToArray();
                    object[] connections = !ConnectWires || ConnectionItems is null ? [] : Enumerable.Repeat<object>(new { from = new { device = tags[0], pin = "14" }, to = new { device = tags[1], pin = WrongPin ? "13" : "14" } }, DuplicateEdge ? 2 : 1).ToArray();
                    result = new { connections, nextCursor = (string?)null, truncated = false };
                    break;
                case "eplan_export_image":
                    result = new { directory = "scratch", fileCount = 1, totalBytes = 100, truncated = false };
                    break;
                default:
                    throw new InvalidOperationException("Unexpected tool: " + tool);
            }
            return Task.FromResult((true, JsonSerializer.SerializeToElement(result)));
        }
    }
}
