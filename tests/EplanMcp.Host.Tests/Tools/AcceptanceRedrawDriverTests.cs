using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Large.Tests;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class AcceptanceRedrawDriverTests
{
    [Fact]
    public async Task NativePointsAndTerminals_UseRealArguments_ReportOnlyUnsupportedItems_AndKeepPages()
    {
        var fake = new DrawingTools { ConnectWires = true };
        var driver = Driver(fake);
        await driver.DrawAsync();
        await driver.ExportAsync();
        Assert.Equal(["eplan_page_create", "eplan_page_open", "eplan_page_get"], fake.Calls.Take(3));
        Assert.Equal("eplan_export_image", fake.Calls[^1]);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("undo", StringComparison.OrdinalIgnoreCase) || c.Contains("delete", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(41, driver.UndoSteps);
        Assert.False(driver.UndoCountIncomplete);
        JsonElement report = JsonSerializer.SerializeToElement(driver.Report);
        Assert.Equal(["pages", "location_boxes", "devices", "plc", "terminals", "wires_symbols_and_cables", "generate", "cables_after_generate", "interruption_points", "texts", "verify"], report.GetProperty("stages").EnumerateArray().Select(n => n.GetString()));
        string[] skipped = report.GetProperty("skipped").EnumerateArray().Select(s => s.GetProperty("item").GetString()!).ToArray();
        Assert.DoesNotContain("plc", skipped);
        Assert.DoesNotContain("strip", skipped);
        Assert.All(Enumerable.Range(0, 7), i => Assert.Contains("input" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/display_properties", skipped));
        Assert.DoesNotContain(skipped, id => id.StartsWith("terminal", StringComparison.Ordinal));
        JsonElement[] placements = fake.Requests.Where(r => r.Tool == "eplan_device_place").Select(r => r.Args).ToArray();
        Assert.Equal(["SSRW", "SSRW", "SSD", "SSD", "SONOT2", "SSD"], placements.Skip(2).Take(6).Select(a => a.GetProperty("symbol").GetString()));
        JsonElement[] evidence = report.GetProperty("evidence").EnumerateArray().Where(e => e.TryGetProperty("tool", out _)).ToArray();
        JsonElement[] switches = evidence.Where(e => e.GetProperty("tool").GetString() == "eplan_device_place")
            .Select(e => e.GetProperty("result")).Where(r => r.TryGetProperty("device", out JsonElement device) && device.GetProperty("category").GetString() == "Switch").ToArray();
        Assert.Equal(6, switches.Length);
        Assert.All(switches, s =>
        {
            JsonElement points = s.GetProperty("symbol").GetProperty("connectionPoints");
            Assert.Equal(2, points.GetArrayLength());
            Assert.Equal(["Up", "Down"], points.EnumerateArray().Select(p => p.GetProperty("direction").GetString()));
            Assert.Equal([0d, 0d, 0d, -8d], points.EnumerateArray().SelectMany(p => p.GetProperty("location").EnumerateArray()).Select(n => n.GetDouble()));
        });
        JsonElement emergencyStop = Assert.Single(evidence, e => e.GetProperty("tool").GetString() == "eplan_device_get" && e.GetProperty("result").GetProperty("device").GetProperty("tag").GetString() == "+Ptest-S5");
        Assert.Equal(["11", "12"], emergencyStop.GetProperty("result").GetProperty("device").GetProperty("pins").EnumerateArray().Select(p => p.GetString()));
        JsonElement[] discovered = evidence.Where(e => e.GetProperty("tool").GetString() == "eplan_symbol_search")
            .SelectMany(e => e.GetProperty("result").GetProperty("symbols").EnumerateArray()).Where(s => s.GetProperty("functionCategory").GetString() == "Switch").ToArray();
        Assert.Equal(["SSD", "SSRW", "SONOT2"], discovered.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(["Pushbutton, normally open contact", "Rotary switch, normally open contact, 2 positions", "Emergency stop pushbutton, normally closed contact, rotary reset"], discovered.Select(s => s.GetProperty("description").GetString()));
        Assert.All(discovered, s =>
        {
            Assert.Equal("IEC_symbol", s.GetProperty("library").GetString());
            Assert.Equal(0, s.GetProperty("variant").GetInt32());
            Assert.True(JsonElement.DeepEquals(switches[0].GetProperty("symbol").GetProperty("connectionPoints"), s.GetProperty("connectionPoints")));
        });
        JsonElement box = Assert.Single(placements, a => a.TryGetProperty("symbol", out JsonElement symbol) && symbol.GetString() == "PLCC");
        Assert.False(box.TryGetProperty("display_properties", out _));
        JsonElement[] inputs = placements.Where(a => a.TryGetProperty("plc_connection_point", out _)).ToArray();
        Assert.Equal(7, inputs.Length);
        Assert.All(inputs, a =>
        {
            Assert.Equal("+A1-KFtest1", a.GetProperty("tag").GetString());
            Assert.Equal("DI_NATIVE", a.GetProperty("symbol").GetString());
            Assert.False(a.TryGetProperty("display_properties", out _));
            Assert.False(a.TryGetProperty("connection_designations", out _));
        });
        JsonElement[] terminals = placements.Where(a => a.TryGetProperty("terminal_strip", out _)).ToArray();
        Assert.Equal(8, terminals.Length);
        Assert.Equal("Fictional meadow input marshalling", terminals[0].GetProperty("terminal_strip_text").GetString());
        Assert.All(terminals.Skip(1), a => Assert.False(a.TryGetProperty("terminal_strip_text", out _)));
        Assert.All(terminals, a => Assert.False(a.TryGetProperty("tag", out _) || a.TryGetProperty("library", out _) || a.TryGetProperty("connection_designations", out _)));
        Assert.Equal(7, skipped.Length);
        Assert.All(skipped, id => Assert.EndsWith("/display_properties", id, StringComparison.Ordinal));
        Assert.Equal(2, fake.Calls.Count(c => c == "eplan_cable_get"));
        Assert.All(report.GetProperty("skipped").EnumerateArray(), item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("reason").GetString())));
    }

    [Fact]
    public async Task Cables_ShareTheWireCall_AndReferenceOnlyItsWireItems()
    {
        var fake = new DrawingTools { ConnectWires = true };
        await new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", ExplicitWiresList(), "test", _ => { }).DrawAsync();
        JsonElement[] calls = fake.Requests.Where(r => r.Tool == "eplan_connection_create").Select(r => r.Args).ToArray();
        Assert.Equal(3, calls.Length);
        JsonElement batch = calls[0].GetProperty("items");
        Assert.Equal(22, batch.GetArrayLength());
        Assert.Equal(13, batch.EnumerateArray().Count(i => i.GetProperty("kind").GetString() == "wire"));
        JsonElement[] definitions = batch.EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "cable_definition").ToArray();
        Assert.Equal(2, definitions.Length);
        JsonElement list = AcceptanceDrawingList.Read(AcceptanceDrawingListTests.Fixture());
        for (int i = 0; i < definitions.Length; i++)
        {
            JsonElement[] expected = list.GetProperty("cables")[i].GetProperty("cores").EnumerateArray().ToArray();
            JsonElement[] cores = definitions[i].GetProperty("cores").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, cores.Length);
            for (int j = 0; j < cores.Length; j++)
            {
                JsonElement reference = cores[j].GetProperty("connection");
                Assert.Single(reference.EnumerateObject());
                int index = reference.GetProperty("item_index").GetInt32();
                Assert.Equal("wire", batch[index].GetProperty("kind").GetString());
                Assert.Equal(expected[j].GetProperty("wire").GetString(), list.GetProperty("wires")[index].GetProperty("id").GetString());
                Assert.Equal(expected[j].GetProperty("label").GetString(), cores[j].GetProperty("label").GetString());
                Assert.Equal(expected[j].GetProperty("colour").GetString(), cores[j].GetProperty("colour").GetString());
            }
        }
        Assert.True(fake.Requests.FindIndex(r => r.Tool == "eplan_cable_get") < fake.Requests.FindIndex(r => r.Tool == "eplan_connection_generate"));
        foreach (JsonElement call in calls.Skip(1))
        {
            JsonElement[] points = call.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "interruption_point").ToArray();
            Assert.Equal(5, points.Length);
            Assert.All(points, p => Assert.Equal(call.GetProperty("page").GetString() == "=GAA+A1&EFS1/test" ? "=GAA+A1&EFS1/test2" : "=GAA+A1&EFS1/test", Assert.Single(p.GetProperty("counterpart_pages").EnumerateArray()).GetString()));
            Assert.Single(call.GetProperty("items").EnumerateArray(), i => i.GetProperty("kind").GetString() == "potential_point");
        }
    }

    [Theory]
    [InlineData("Up")]
    [InlineData("Down")]
    [InlineData("Left")]
    [InlineData("Right")]
    public async Task AlignedAnchors_GenerateLogicalConnection_WithoutDrawingAWire(string direction)
    {
        var fake = new DrawingTools { ConnectWires = true, PaginateDrawings = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AutoconnectList(direction), "test", _ => { });
        await driver.DrawAsync();
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
        Assert.Contains("eplan_connection_generate", fake.Calls);
        JsonElement evidence = Assert.Single(JsonSerializer.SerializeToElement(driver.Report).GetProperty("evidence").EnumerateArray(), e => e.TryGetProperty("wire", out _));
        Assert.Equal("testwire", evidence.GetProperty("wire").GetString());
        Assert.Equal("autoconnect", evidence.GetProperty("mode").GetString());
        Assert.True(fake.ContinuationReads > 0);
        Assert.Equal(23, driver.UndoSteps);
    }

    [Theory]
    [InlineData("axis")]
    [InlineData("direction")]
    [InlineData("away")]
    [InlineData("coincident")]
    public async Task InvalidAutoconnectAnchors_FailWithWireIdAndBendPointGuidance(string fault)
    {
        JsonObject list = JsonNode.Parse(AutoconnectList("Down").GetRawText())!.AsObject();
        if (fault == "axis")
        { list["devices"]![1]!["position"]![0] = 52; }
        else if (fault == "direction")
        { list["wires"]![0]!["to"]!["pin"] = "14"; }
        else
        { list["devices"]![1]!["position"]![1] = fault == "away" ? 240 : 204; }
        var fake = new DrawingTools { ConnectWires = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(list.ToJsonString()), "test", _ => { });
        var error = await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
        Assert.Contains("testwire", error.Message, StringComparison.Ordinal);
        Assert.Contains("do not face each other on one axis", error.Message, StringComparison.Ordinal);
        Assert.Contains("explicit bend points", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
        Assert.DoesNotContain("eplan_connection_generate", fake.Calls);
    }

    [Fact]
    public async Task AutoconnectCables_AfterGenerate_UseExactDetailedLocatorsAcrossAllPages()
    {
        var fake = new DrawingTools { ConnectWires = true, PaginateConnections = true };
        var driver = Driver(fake);
        await driver.DrawAsync();
        JsonElement[] calls = fake.Requests.Where(r => r.Tool == "eplan_connection_create").Select(r => r.Args).ToArray();
        Assert.Equal(4, calls.Length);
        Assert.Equal(7, calls[0].GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(calls.SelectMany(c => c.GetProperty("items").EnumerateArray()), i => i.GetProperty("kind").GetString() == "wire");
        int generated = fake.Requests.FindIndex(r => r.Tool == "eplan_connection_generate");
        int cableCall = fake.Requests.FindIndex(generated, r => r.Tool == "eplan_connection_create");
        Assert.True(generated < cableCall);
        Assert.All(fake.Requests.Skip(generated + 1).Take(cableCall - generated - 1), r =>
        {
            Assert.Equal("eplan_connection_list", r.Tool);
            Assert.Equal("detailed", r.Args.GetProperty("response_format").GetString());
        });
        Assert.True(cableCall - generated > 2);
        JsonElement[] listed = JsonSerializer.SerializeToElement(driver.Report).GetProperty("evidence").EnumerateArray()
            .Where(e => e.TryGetProperty("tool", out JsonElement tool) && tool.GetString() == "eplan_connection_list")
            .SelectMany(e => e.GetProperty("result").GetProperty("connections").EnumerateArray()).Where(c => c.TryGetProperty("locator", out _)).ToArray();
        JsonElement[] definitions = calls[1].GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, definitions.Length);
        foreach (JsonElement core in definitions.SelectMany(c => c.GetProperty("cores").EnumerateArray()))
        {
            JsonElement reference = core.GetProperty("connection");
            Assert.Single(reference.EnumerateObject());
            Assert.Contains(listed, c => JsonElement.DeepEquals(c.GetProperty("locator"), reference.GetProperty("locator")));
        }
        Assert.True(fake.Requests.FindIndex(r => r.Tool == "eplan_cable_get") > cableCall);
    }

    [Fact]
    public async Task AutoconnectOnlyCable_ReversedEndpoints_CreatesOnlyDefinitionAfterGenerate()
    {
        JsonObject list = JsonNode.Parse(AutoconnectList("Up").GetRawText())!.AsObject();
        list["cables"] = JsonNode.Parse("""[{"id":"testcable","tag":"=GAA+A1-W{run}","type_text":"Fictional test cable","core_count":1,"cross_section_mm2":0.5,"points":[[40,180],[56,180]],"cores":[{"wire":"testwire","label":"1","colour":"RD"}]}]""");
        var fake = new DrawingTools { ConnectWires = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(list.ToJsonString()), "test", _ => { });
        await driver.DrawAsync();
        JsonElement batch = Assert.Single(fake.Requests, r => r.Tool == "eplan_connection_create").Args.GetProperty("items");
        JsonElement definition = Assert.Single(batch.EnumerateArray());
        Assert.Equal("cable_definition", definition.GetProperty("kind").GetString());
        Assert.True(Assert.Single(definition.GetProperty("cores").EnumerateArray()).GetProperty("connection").TryGetProperty("locator", out _));
        Assert.True(fake.Calls.IndexOf("eplan_connection_create") > fake.Calls.IndexOf("eplan_connection_generate"));
        Assert.Single(fake.Calls, c => c == "eplan_cable_get");
    }

    [Fact]
    public async Task MixedCables_KeepExplicitOnlyItemIndices_AndDeferMixedDefinitionOnce()
    {
        JsonObject list = JsonNode.Parse(ExplicitWiresList().GetRawText())!.AsObject();
        list["wires"]![0]!["points"] = new JsonArray("from", "to");
        var fake = new DrawingTools { ConnectWires = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(list.ToJsonString()), "test", _ => { });
        await driver.DrawAsync();
        JsonElement[] batches = fake.Requests.Where(r => r.Tool == "eplan_connection_create").Select(r => r.Args.GetProperty("items")).ToArray();
        Assert.Equal(12, batches[0].EnumerateArray().Count(i => i.GetProperty("kind").GetString() == "wire"));
        JsonElement early = Assert.Single(batches[0].EnumerateArray(), i => i.GetProperty("kind").GetString() == "cable_definition");
        Assert.EndsWith("4", early.GetProperty("designation").GetString(), StringComparison.Ordinal);
        int index = Assert.Single(early.GetProperty("cores").EnumerateArray()).GetProperty("connection").GetProperty("item_index").GetInt32();
        Assert.Equal(11, index);
        Assert.Equal("wire", batches[0][index].GetProperty("kind").GetString());
        JsonElement mixed = Assert.Single(batches[1].EnumerateArray());
        Assert.EndsWith("3", mixed.GetProperty("designation").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, mixed.GetProperty("cores").GetArrayLength());
        Assert.All(mixed.GetProperty("cores").EnumerateArray(), c => Assert.True(c.GetProperty("connection").TryGetProperty("locator", out _)));
        int generate = fake.Requests.FindIndex(r => r.Tool == "eplan_connection_generate");
        Assert.True(fake.Requests.FindIndex(r => r.Tool == "eplan_cable_get") < generate);
        Assert.True(fake.Requests.FindLastIndex(r => r.Tool == "eplan_cable_get") > generate);
        Assert.Equal(2, fake.Calls.Count(c => c == "eplan_cable_get"));
    }

    [Theory]
    [InlineData("missing", "exactly one generated connection")]
    [InlineData("duplicate", "exactly one generated connection")]
    [InlineData("locator", "requires a locator")]
    [InlineData("truncated", "truncated without a continuation cursor")]
    public async Task GeneratedCableConnection_IncompleteOrAmbiguousEvidence_FailsBeforeCableWrite(string fault, string message)
    {
        var fake = new DrawingTools { ConnectWires = true, ConnectionFault = fault };
        var error = await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => Driver(fake).DrawAsync());
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Single(fake.Calls, c => c == "eplan_connection_create");
        Assert.Contains("eplan_connection_generate", fake.Calls);
        Assert.DoesNotContain("eplan_cable_get", fake.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public async Task AutoconnectExpectedConnection_MissingOrDuplicated_FailsVerification(string fault)
    {
        var fake = new DrawingTools { ConnectWires = true, ConnectionFault = fault };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AutoconnectList("Down"), "test", _ => { });
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
    }

    [Theory]
    [InlineData("actual.designation")]
    [InlineData("actual.type")]
    [InlineData("actual.label")]
    [InlineData("actual.colour")]
    [InlineData("actual.oid")]
    [InlineData("actual.pins")]
    [InlineData("actual.count")]
    [InlineData("stored.designation")]
    [InlineData("stored.type")]
    [InlineData("stored.label")]
    [InlineData("stored.colour")]
    [InlineData("stored.count")]
    [InlineData("stored.pin")]
    [InlineData("stored.truncated")]
    public async Task CableReadback_MismatchFailsWithoutSkippingOrRetry(string fault)
    {
        var fake = new DrawingTools { ConnectWires = true, CableFault = fault };
        var driver = Driver(fake);
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
        Assert.Equal(2, fake.Calls.Count(c => c == "eplan_connection_create"));
        Assert.Contains("eplan_connection_generate", fake.Calls);
        Assert.Equal(34, driver.UndoSteps);
        Assert.All(JsonSerializer.SerializeToElement(driver.Report).GetProperty("skipped").EnumerateArray(), s => Assert.EndsWith("/display_properties", s.GetProperty("item").GetString(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("page")]
    [InlineData("missing")]
    [InlineData("oid")]
    public async Task InterruptionReadback_WrongOrMissingCounterpartFails(string fault)
    {
        var fake = new DrawingTools { ConnectWires = true, CounterpartFault = fault };
        var driver = Driver(fake);
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
        Assert.Equal(4, fake.Calls.Count(c => c == "eplan_connection_create"));
        Assert.DoesNotContain("eplan_export_image", fake.Calls);
    }

    [Theory]
    [InlineData("address")]
    [InlineData("symbolic_address")]
    [InlineData("text")]
    [InlineData("owner")]
    public async Task PlcReadback_WrongNativeValue_FailsWithoutTreatingThePointAsUnsupported(string field)
    {
        var fake = new DrawingTools { WrongPlcField = field };
        var driver = Driver(fake);
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
        Assert.Equal(17, driver.UndoSteps);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TerminalReadback_MissingMemberOrWrongText_FailsAfterCommit(bool missingMember, bool wrongText)
    {
        var fake = new DrawingTools { MissingMember = missingMember, WrongStripText = wrongText };
        var driver = Driver(fake);
        await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(driver.DrawAsync);
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
        Assert.Equal(25, driver.UndoSteps);
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
        Assert.Equal(24, driver.UndoSteps);
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
        var fake = new DrawingTools { ConnectWires = true, MissingPoint = true };
        var driver = new AcceptanceRedrawDriver(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(list.ToJsonString()), "test", _ => { });
        await driver.DrawAsync();
        Assert.Null(fake.ConnectionItems);
        Assert.DoesNotContain("eplan_connection_create", fake.Calls);
        Assert.Equal(21, driver.UndoSteps);
        Assert.True(driver.SkippedCount > 0);
    }

    private static JsonElement TwoSwitches()
    {
        JsonObject list = JsonNode.Parse(AcceptanceDrawingListTests.Fixture())!.AsObject();
        list["devices"] = new JsonArray(list["devices"]![0]!.DeepClone(), list["devices"]![1]!.DeepClone());
        // Keep unrelated placed pins off the two switches' axes and each other's axes.
        foreach (JsonNode? terminal in list["terminal_strip"]!["terminals"]!.AsArray())
        { terminal!["position"]![0] = terminal["position"]![0]!.GetValue<int>() + 4; }
        foreach (JsonNode? input in list["plc"]!["points"]!.AsArray())
        { input!["position"]![0] = input["position"]![0]!.GetValue<int>() + 8; }
        list["wires"] = JsonNode.Parse("""[{"id":"testwire","from":{"item":"switch1","pin":"14"},"to":{"item":"switch2","pin":"14"},"points":["from",[48,188],[80,188],"to"]}]""");
        list["expected_connections"] = JsonNode.Parse("""[{"from":{"item":"switch1","pin":"14"},"to":{"item":"switch2","pin":"14"}}]""");
        list["nodes"] = new JsonArray();
        list["cables"] = new JsonArray();
        list["interruption_points"] = new JsonArray();
        return AcceptanceDrawingList.Read(list.ToJsonString());
    }

    private static JsonElement AutoconnectList(string direction)
    {
        JsonObject list = JsonNode.Parse(TwoSwitches().GetRawText())!.AsObject();
        bool horizontal = direction is "Left" or "Right";
        list["devices"]![1]!["position"] = horizontal ? new JsonArray(96, 212) : new JsonArray(48, 160);
        foreach (JsonNode? device in list["devices"]!.AsArray())
        { device!["variant"] = horizontal ? 1 : 0; }
        list["wires"]![0]!["to"]!["pin"] = "13";
        list["wires"]![0]!["points"] = new JsonArray("from", "to");
        if (direction is "Up" or "Left")
        {
            JsonNode from = list["wires"]![0]!["from"]!.DeepClone();
            list["wires"]![0]!["from"] = list["wires"]![0]!["to"]!.DeepClone();
            list["wires"]![0]!["to"] = from;
        }
        list["expected_connections"] = new JsonArray(new JsonObject { ["from"] = list["wires"]![0]!["from"]!.DeepClone(), ["to"] = list["wires"]![0]!["to"]!.DeepClone() });
        return AcceptanceDrawingList.Read(list.ToJsonString());
    }

    internal static JsonElement ExplicitWiresList()
    {
        JsonObject list = JsonNode.Parse(AcceptanceDrawingListTests.Fixture())!.AsObject();
        foreach (JsonNode? wire in list["wires"]!.AsArray())
        {
            bool signal = wire!["id"]!.GetValue<string>().StartsWith("signal", StringComparison.Ordinal);
            string id = wire["from"]!["item"]!.GetValue<string>();
            JsonNode source = (signal ? list["devices"] : list["terminal_strip"]!["terminals"])!.AsArray().Single(d => d!["id"]!.GetValue<string>() == id)!;
            wire["points"] = new JsonArray("from", new JsonArray(source["position"]![0]!.GetValue<int>(), signal ? 160 : 88), "to");
        }
        return AcceptanceDrawingList.Read(list.ToJsonString());
    }

    private static AcceptanceRedrawDriver Driver(DrawingTools fake) => new(fake.CallAsync, "LargeWrite", AcceptanceDrawingList.Read(AcceptanceDrawingListTests.Fixture()), "test", _ => { });

    internal sealed class DrawingTools
    {
        private static readonly Dictionary<string, string> _switchDescriptions = new(StringComparer.Ordinal)
        {
            ["SSD"] = "Pushbutton, normally open contact",
            ["SSRW"] = "Rotary switch, normally open contact, 2 positions",
            ["SONOT2"] = "Emergency stop pushbutton, normally closed contact, rotary reset",
        };

        internal List<string> Calls { get; } = [];
        internal List<(string Tool, JsonElement Args)> Requests { get; } = [];
        internal bool MissingPoint { get; init; }
        internal string? WrongPlcField { get; init; }
        internal bool MissingMember { get; init; }
        internal bool WrongStripText { get; init; }
        internal string? FailPlace { get; init; }
        internal bool ConnectWires { get; init; }
        internal bool PaginateDrawings { get; init; }
        internal bool PaginateConnections { get; init; }
        internal string? ConnectionFault { get; init; }
        internal bool WrongPin { get; init; }
        internal bool DuplicateEdge { get; init; }
        internal string? CableFault { get; init; }
        internal string? CounterpartFault { get; init; }
        internal JsonElement? ConnectionItems { get; private set; }
        internal int ContinuationReads { get; private set; }
        private readonly List<object> _drawings = [];
        private readonly Dictionary<string, string[]> _pins = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> _functions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement[]> _symbolPoints = new(StringComparer.Ordinal);
        private readonly List<JsonElement> _connections = [];
        private readonly Dictionary<long, JsonElement> _connectionWires = [];
        private readonly List<JsonElement> _cables = [];
        private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);
        private readonly List<object> _channels = [];
        private readonly List<object> _members = [];
        private readonly List<JsonElement> _interruptions = [];
        private readonly Dictionary<string, (string Tag, string Pin)> _support = new(StringComparer.Ordinal);
        private string? _connectionPage;
        private string? _stripText;
        private long _oid;

        private static string Stored(string tag) => tag.StartsWith('=') ? tag[tag.IndexOf('+', StringComparison.Ordinal)..] : tag;

        private static JsonElement[] ConnectionPoints(int count, bool horizontal = false) => Enumerable.Range(0, count)
            .Select(i => JsonSerializer.SerializeToElement(new { location = horizontal ? new[] { 8 * i, 0 } : new[] { 0, -8 * i }, direction = horizontal ? i == 0 ? "Left" : "Right" : i == 0 ? "Up" : "Down" })).ToArray();

        internal Task<(bool Succeeded, JsonElement Result)> CallAsync(string tool, Dictionary<string, object?> args)
        {
            Calls.Add(tool);
            Requests.Add((tool, JsonSerializer.SerializeToElement(args)));
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
                        bool terminal = args.TryGetValue("terminal_strip", out object? strip);
                        bool input = args.TryGetValue("plc_connection_point", out object? point);
                        if (input && MissingPoint && (string)point! == "1")
                        { return Task.FromResult((false, JsonSerializer.SerializeToElement(new { reason = ErrorReason.CapabilityMissing, execution = "not_started" }))); }
                        string parent = terminal ? Stored((string)strip!) : Stored((string)args["tag"]!);
                        string tag = terminal ? parent + ":" + args["terminal_number"] : input ? parent + ":" + point : parent;
                        string[] pins = terminal ? ["1", "2"] : input ? [(string)point!] : ((JsonElement)args["connection_designations"]!).EnumerateArray().Select(n => n.GetString()!).ToArray();
                        string? description = terminal || input ? null : _switchDescriptions.GetValueOrDefault((string)args["symbol"]!);
                        string category = terminal ? "Terminal" : input ? "PLCTerminal" : (string)args["symbol"]! == "PLCC" ? "PLCBox" : description is not null ? "Switch" : "NoContact";
                        if (category == "Switch")
                        { Assert.Equal(2, pins.Length); }
                        _pins[tag] = pins;
                        bool horizontal = (int)args["symbol_variant"]! == 1;
                        _symbolPoints[tag] = ConnectionPoints(pins.Length, horizontal);
                        if (terminal || input)
                        { _owners[tag] = parent; }
                        if (args.TryGetValue("terminal_strip_text", out object? text))
                        { _stripText = (string)text!; _functions[parent] = JsonSerializer.SerializeToElement(new { tag = parent, category = "TerminalStrip", pins = Array.Empty<string>(), page = args["page"], text }); }
                        _functions[tag] = JsonSerializer.SerializeToElement(new { tag, category, pins, page = args["page"], text = (string?)null });
                        _drawings.Add(new { page = args["page"], kind = terminal || input ? "Terminal" : "Function", properties = new { name = "=" + tag }, location = new[] { args["x"], args["y"] }, locator = new { project = "LargeWrite", page = args["page"], type = 1, oid = ++_oid, digest = new string('a', 64) } });
                        object? member = null;
                        if (terminal)
                        {
                            member = new { strip = parent, number = args["terminal_number"], owner = (string?)null, stripCreated = text is not null };
                            _members.Add(new { name = "=" + tag, designation = args["terminal_number"], page = args["page"] });
                        }
                        if (input)
                        {
                            string? Field(string field) => WrongPlcField == field ? "wrong" : (string?)args["plc_" + field];
                            _channels.Add(new { name = tag, owner = WrongPlcField == "owner" ? "+OTHER-KF1" : parent, page = args["page"], address = Field("address"), manualSymbolicAddress = Field("symbolic_address"), symbolicAddress = Field("address"), text = Field("text"), parentlessRepresentations = 1 });
                            _channels.Add(new { name = parent, address = (string?)null });
                        }
                        result = new { execution = "committed", undo = "Device", recovered = false, device = new { tag, category }, terminal = member, symbol = new { description, connectionPoints = _symbolPoints[tag] } };
                    }
                    break;
                case "eplan_device_get":
                    string owner = (string)args["tag"]!;
                    JsonElement main = _functions[owner];
                    if (WrongStripText && main.GetProperty("category").GetString() == "TerminalStrip")
                    { main = JsonSerializer.SerializeToElement(new { tag = owner, category = "TerminalStrip", pins = Array.Empty<string>(), text = _stripText + "wrong" }); }
                    result = new { device = main, functions = _owners.Where(p => p.Value == owner).Select(p => _functions[p.Key]), parts = Array.Empty<object>(), truncated = false };
                    break;
                case "eplan_plc_get":
                    result = new { channels = _channels, truncated = false };
                    break;
                case "eplan_terminal_strip_get":
                    result = new { strip = new { name = args["strip"] }, terminals = _members.Skip(MissingMember ? 1 : 0), truncated = false };
                    break;
                case "eplan_symbol_search":
                    bool second = args["cursor"] is not null;
                    object inputSymbol = new { library = "IEC_symbol", name = "DI_NATIVE", description = "Digital input connection point", variant = 0, functionCategory = "PLCTerminal", plcInputDefinition = second ? "1/1" : null, connectionPoints = ConnectionPoints(1) };
                    object[] symbols = second ? [inputSymbol] : [.. _switchDescriptions.Select(s => (object)new { library = "IEC_symbol", name = s.Key, description = s.Value, variant = 0, functionCategory = "Switch", connectionPoints = ConnectionPoints(2) }), inputSymbol];
                    result = new { symbols, nextCursor = second ? null : "next", truncated = !second };
                    break;
                case "eplan_page_get":
                    object[] drawings = _drawings.Where(d => JsonSerializer.SerializeToElement(d).GetProperty("page").GetString() == (string)args["page"]!).ToArray();
                    int offset = args.TryGetValue("drawing_cursor", out object? cursor) ? int.Parse((string)cursor!, System.Globalization.CultureInfo.InvariantCulture) : 0;
                    if (cursor is not null)
                    { ContinuationReads++; }
                    int count = PaginateDrawings ? 1 : drawings.Length;
                    string? next = offset + count < drawings.Length ? (offset + count).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                    result = new { drawings = new { gridSize = 4, pageSize = new[] { 420, 297 }, items = drawings.Skip(offset).Take(count), nextCursor = next, truncated = next is not null } };
                    break;
                case "eplan_connection_generate":
                    int before = _connections.Count;
                    if (ConnectWires)
                    {
                        foreach (string page in (string[])args["pages"]!)
                        { GenerateAutoconnect(page); }
                    }
                    result = new { execution = _connections.Count > before ? "committed" : "not_started", undoSteps = 1 };
                    break;
                case "eplan_connection_create":
                    if (!ConnectWires)
                    { return Task.FromResult((false, JsonSerializer.SerializeToElement(new { reason = ErrorReason.CapabilityMissing, execution = "not_started", message = "No new electrical connection to generate for an isolated interruption point." }))); }
                    JsonElement items = JsonSerializer.SerializeToElement(args["items"]);
                    if (items[0].GetProperty("kind").GetString() == "interruption_point")
                    { result = Interruptions(args, items); }
                    else
                    {
                        string page = (string)args["page"]!;
                        if (items.EnumerateArray().Any(i => i.GetProperty("kind").GetString() != "cable_definition"))
                        { ConnectionItems = items; _connectionPage = page; }
                        foreach (JsonElement wire in items.EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "wire"))
                        { AddConnection(wire, page); }
                        JsonElement[] cables = items.EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "cable_definition").ToArray();
                        _cables.AddRange(cables);
                        int steps = cables.Length == 0 ? 2 : 6 + (items.EnumerateArray().Any(i => i.GetProperty("kind").GetString() == "wire") ? 1 : 0) + (items.EnumerateArray().Any(i => i.GetProperty("kind").GetString() is "corner" or "t_node" or "potential_point") ? 1 : 0);
                        result = new { execution = "committed", undoSteps = steps, actualCores = ActualCores(items) };
                    }
                    break;
                case "eplan_cable_get":
                    JsonElement cable = Assert.Single(_cables, c => Stored(c.GetProperty("designation").GetString()!) == (string)args["cable"]!);
                    result = new
                    {
                        cable = new { tag = args["cable"], type = cable.GetProperty("type_text"), wireCount = cable.GetProperty("core_count").GetRawText(), crossSection = cable.GetProperty("cross_section_mm2").GetRawText().Replace('.', ',') },
                        cores = cable.GetProperty("cores").EnumerateArray().Reverse().Select(c => new { designation = c.GetProperty("label"), colour = c.GetProperty("colour"), from = End(CoreWire(c, ConnectionItems), "from"), to = End(CoreWire(c, ConnectionItems), "to") }),
                        nextCursor = (string?)null,
                        truncated = false,
                    };
                    break;
                case "eplan_placement_create":
                    foreach (JsonElement item in JsonSerializer.SerializeToElement(args["items"]).EnumerateArray())
                    { _drawings.Add(new { page = args["page"], kind = "Text", properties = new { text = "??_??@" + item.GetProperty("contents").GetProperty("??_??").GetString() + ";" }, location = item.GetProperty("position") }); }
                    result = new { execution = "committed" };
                    break;
                case "eplan_connection_list":
                    var connections = new List<object>();
                    connections.AddRange(_connections.Where(c => c.GetProperty("page").GetString() == (string)args["page"]!).Select(c => (object)c));
                    if (ConnectWires && (string)args["page"]! == _connectionPage && ConnectionItems is { } batch)
                    {
                        connections.AddRange(NodeConnections(batch));
                    }
                    if ((DuplicateEdge || ConnectionFault == "duplicate") && connections.Count > 0)
                    { connections.Add(connections[0]); }
                    if (_support.TryGetValue((string)args["page"]!, out var support))
                    { connections.Add(new { from = (object?)null, to = new { device = support.Tag, pin = support.Pin } }); }
                    if (ConnectionFault == "missing")
                    { connections.Clear(); }
                    JsonObject[] rows = connections.Select(c => JsonNode.Parse(JsonSerializer.Serialize(c))!.AsObject()).ToArray();
                    foreach (JsonObject row in rows)
                    {
                        if (!args.TryGetValue("response_format", out object? format) || (string)format! != "detailed" || ConnectionFault == "locator")
                        { row.Remove("locator"); }
                    }
                    int connectionOffset = args.TryGetValue("cursor", out object? connectionCursor) ? int.Parse((string)connectionCursor!, System.Globalization.CultureInfo.InvariantCulture) : 0;
                    int connectionCount = PaginateConnections ? 1 : rows.Length;
                    string? connectionNext = connectionOffset + connectionCount < rows.Length ? (connectionOffset + connectionCount).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                    result = new { connections = rows.Skip(connectionOffset).Take(connectionCount), nextCursor = connectionNext, truncated = connectionNext is not null || ConnectionFault == "truncated" };
                    break;
                case "eplan_export_image":
                    result = new { directory = "scratch", fileCount = 1, totalBytes = 100, truncated = false };
                    break;
                default:
                    throw new InvalidOperationException("Unexpected tool: " + tool);
            }
            JsonObject data = JsonSerializer.SerializeToNode(result)!.AsObject();
            if (CableFault is { } fault && (fault.StartsWith("actual.", StringComparison.Ordinal) && tool == "eplan_connection_create" && data["actualCores"] is JsonArray { Count: > 0 } || fault.StartsWith("stored.", StringComparison.Ordinal) && tool == "eplan_cable_get"))
            {
                bool actual = fault.StartsWith("actual.", StringComparison.Ordinal);
                string field = fault[(fault.IndexOf('.', StringComparison.Ordinal) + 1)..];
                JsonArray cores = data[actual ? "actualCores" : "cores"]!.AsArray();
                if (field == "count")
                { cores.RemoveAt(0); }
                else if (field == "truncated")
                { data["truncated"] = true; }
                else if (!actual && field is "designation" or "type")
                { data["cable"]![field == "designation" ? "tag" : "type"] = "+OTHER-W3"; }
                else if (!actual && field == "pin")
                { cores[0]!["from"]!["pin"] = "wrong"; }
                else
                { cores[0]![!actual && field == "label" ? "designation" : field] = field == "oid" ? JsonValue.Create(0) : field == "pins" ? new JsonArray("wrong") : JsonValue.Create("wrong"); }
            }
            return Task.FromResult((true, JsonSerializer.SerializeToElement(data)));
        }

        private object End(JsonElement wire, string key)
        {
            JsonElement reference = wire.GetProperty(key);
            JsonElement row = Assert.Single(_drawings.Select(d => JsonSerializer.SerializeToElement(d)), d => d.TryGetProperty("locator", out JsonElement locator) && locator.GetProperty("oid").GetInt64() == reference.GetProperty("locator").GetProperty("oid").GetInt64());
            string tag = row.GetProperty("properties").GetProperty("name").GetString()![1..];
            return new { device = "=" + tag, pin = WrongPin ? "wrong" : _pins[tag][reference.GetProperty("pin_index").GetInt32()] };
        }

        private JsonElement CoreWire(JsonElement core, JsonElement? items)
        {
            JsonElement reference = core.GetProperty("connection");
            Assert.Single(reference.EnumerateObject());
            if (reference.TryGetProperty("locator", out JsonElement locator))
            {
                JsonElement connection = Assert.Single(_connections, c => c.GetProperty("locator").GetProperty("oid").GetInt64() == locator.GetProperty("oid").GetInt64());
                Assert.True(JsonElement.DeepEquals(locator, connection.GetProperty("locator")));
                return _connectionWires[locator.GetProperty("oid").GetInt64()];
            }
            JsonElement wire = items!.Value[reference.GetProperty("item_index").GetInt32()];
            Assert.Equal("wire", wire.GetProperty("kind").GetString());
            return wire;
        }

        private IEnumerable<object> ActualCores(JsonElement items)
        {
            foreach (JsonElement cable in items.EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "cable_definition"))
            {
                foreach (JsonElement core in cable.GetProperty("cores").EnumerateArray())
                {
                    JsonElement wire = CoreWire(core, items);
                    string Pin(string end)
                    {
                        JsonElement pin = wire.GetProperty(end), locator = pin.GetProperty("locator");
                        return FormattableString.Invariant($"{locator.GetProperty("type").GetInt32()}/{locator.GetProperty("oid").GetInt64()}/{pin.GetProperty("pin_index").GetInt32()}");
                    }
                    long oid = Assert.Single(_connectionWires, c => JsonElement.DeepEquals(c.Value, wire)).Key;
                    yield return new { designation = Stored(cable.GetProperty("designation").GetString()!), type = cable.GetProperty("type_text"), label = core.GetProperty("label"), colour = core.GetProperty("colour"), oid, pins = new[] { Pin("to"), Pin("from") } };
                }
            }
        }

        private void AddConnection(JsonElement wire, string page)
        {
            long oid = 1000 + _connections.Count;
            _connectionWires.Add(oid, wire);
            _connections.Add(JsonSerializer.SerializeToElement(new { from = End(wire, "from"), to = End(wire, "to"), page, locator = new { project = "LargeWrite", page, type = 9, oid, digest = new string('b', 64) } }));
        }

        private void GenerateAutoconnect(string page)
        {
            var ports = _drawings.Select(d => JsonSerializer.SerializeToElement(d)).Where(d => d.GetProperty("page").GetString() == page && d.TryGetProperty("locator", out _)).SelectMany(row =>
            {
                string tag = row.GetProperty("properties").GetProperty("name").GetString()![1..];
                return _symbolPoints[tag].Select((p, i) => new
                {
                    X = row.GetProperty("location")[0].GetDouble() + p.GetProperty("location")[0].GetDouble(),
                    Y = row.GetProperty("location")[1].GetDouble() + p.GetProperty("location")[1].GetDouble(),
                    Direction = p.GetProperty("direction").GetString(),
                    Reference = new { locator = row.GetProperty("locator"), pin_index = i },
                });
            }).ToArray();
            foreach (var from in ports.Where(p => p.Direction is "Down" or "Right"))
            {
                var to = ports.Where(p => from.Direction == "Down" ? p.Direction == "Up" && p.X == from.X && p.Y < from.Y : p.Direction == "Left" && p.Y == from.Y && p.X > from.X)
                    .OrderBy(p => Math.Abs(p.X - from.X) + Math.Abs(p.Y - from.Y)).FirstOrDefault();
                if (to is null)
                { continue; }
                JsonElement wire = JsonSerializer.SerializeToElement(new { kind = "wire", from = from.Reference, to = to.Reference });
                if (_connectionWires.Values.Any(w => JsonElement.DeepEquals(w.GetProperty("from"), wire.GetProperty("from")) && JsonElement.DeepEquals(w.GetProperty("to"), wire.GetProperty("to"))
                    || JsonElement.DeepEquals(w.GetProperty("from"), wire.GetProperty("to")) && JsonElement.DeepEquals(w.GetProperty("to"), wire.GetProperty("from"))))
                { continue; }
                AddConnection(wire, page);
            }
        }

        private object Interruptions(Dictionary<string, object?> args, JsonElement items)
        {
            string page = (string)args["page"]!;
            JsonElement potential = Assert.Single(items.EnumerateArray(), i => i.GetProperty("kind").GetString() == "potential_point");
            Assert.Equal("Down", Assert.Single(potential.GetProperty("directions").EnumerateArray()).GetString());
            JsonElement helper = Assert.Single(_drawings.Select(d => JsonSerializer.SerializeToElement(d)), d => d.GetProperty("page").GetString() == page && d.GetProperty("location")[0].GetDouble() == potential.GetProperty("position")[0].GetDouble() && d.GetProperty("location")[1].GetDouble() == potential.GetProperty("position")[1].GetDouble() - 16);
            string tag = helper.GetProperty("properties").GetProperty("name").GetString()![1..];
            _support.Add(page, (tag, _pins[tag][0]));
            var result = new List<JsonObject>();
            foreach (JsonElement point in items.EnumerateArray().Where(i => i.GetProperty("kind").GetString() == "interruption_point"))
            {
                string peer = Assert.Single(point.GetProperty("counterpart_pages").EnumerateArray()).GetString()!;
                Assert.NotEqual(page, peer);
                string name = point.GetProperty("name").GetString()!;
                _interruptions.Add(JsonSerializer.SerializeToElement(new { name, page, position = point.GetProperty("position"), direction = point.GetProperty("direction"), oid = ++_oid }));
                JsonElement[] pair = _interruptions.Where(p => p.GetProperty("name").GetString() == name).ToArray();
                foreach (JsonElement entry in pair)
                {
                    JsonObject row = JsonNode.Parse(entry.GetRawText())!.AsObject();
                    row["counterparts"] = JsonSerializer.SerializeToNode(pair.Where(p => p.GetProperty("page").GetString() != entry.GetProperty("page").GetString()).Select(p => new { name, page = p.GetProperty("page"), oid = p.GetProperty("oid") }));
                    if (pair.Length == 2 && CounterpartFault is { } fault)
                    {
                        JsonArray counterparts = row["counterparts"]!.AsArray();
                        if (fault == "missing")
                        { counterparts.Clear(); }
                        else
                        { counterparts[0]![fault] = fault == "oid" ? JsonValue.Create(0) : JsonValue.Create("wrong"); }
                    }
                    result.Add(row);
                }
            }
            return new { execution = "committed", undoSteps = 2, interruptionPoints = result };
        }

        private IEnumerable<object> NodeConnections(JsonElement batch)
        {
            var ports = new List<(string Key, double X, double Y, string Direction, object? End)>();
            foreach (JsonElement row in _drawings.Select(d => JsonSerializer.SerializeToElement(d)).Where(d => d.TryGetProperty("locator", out _)))
            {
                string tag = row.GetProperty("properties").GetProperty("name").GetString()![1..];
                for (int i = 0; i < _pins[tag].Length; i++)
                { ports.Add((tag + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), row.GetProperty("location")[0].GetDouble(), row.GetProperty("location")[1].GetDouble() - 8 * i, i == 0 ? "Up" : "Down", new { device = tag, pin = _pins[tag][i] })); }
            }
            JsonElement[] nodes = batch.EnumerateArray().Where(i => i.GetProperty("kind").GetString() is "corner" or "t_node" or "potential_point").ToArray();
            for (int i = 0; i < nodes.Length; i++)
            {
                foreach (JsonElement direction in nodes[i].GetProperty("directions").EnumerateArray())
                { ports.Add(("node/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), nodes[i].GetProperty("position")[0].GetDouble(), nodes[i].GetProperty("position")[1].GetDouble(), direction.GetString()!, null)); }
            }
            var groups = ports.Select(p => p.Key).Distinct(StringComparer.Ordinal).ToDictionary(k => k, k => new HashSet<string>([k], StringComparer.Ordinal), StringComparer.Ordinal);
            static string Opposite(string direction) => direction switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", _ => "Left" };
            foreach (var port in ports.Where(p => p.End is null))
            {
                var candidates = ports.Where(p => p.Direction == Opposite(port.Direction) && (port.Direction switch
                {
                    "Up" => p.X == port.X && p.Y > port.Y,
                    "Down" => p.X == port.X && p.Y < port.Y,
                    "Left" => p.Y == port.Y && p.X < port.X,
                    _ => p.Y == port.Y && p.X > port.X,
                })).OrderBy(p => Math.Abs(p.X - port.X) + Math.Abs(p.Y - port.Y)).ToArray();
                if (candidates.Length == 0)
                { continue; }
                var group = new HashSet<string>(groups[port.Key].Concat(groups[candidates[0].Key]), StringComparer.Ordinal);
                foreach (string key in group)
                { groups[key] = group; }
            }
            foreach (HashSet<string> group in groups.Values.Distinct().Where(g => g.Any(k => k.StartsWith("node/", StringComparison.Ordinal))))
            {
                var ends = ports.Where(p => p.End is not null && group.Contains(p.Key)).OrderByDescending(p => _functions[p.Key[..p.Key.LastIndexOf('/')]].GetProperty("category").GetString() == "Terminal").ToArray();
                foreach (var end in ends.Skip(1))
                { yield return new { from = ends[0].End, to = end.End }; }
            }
        }
    }
}
