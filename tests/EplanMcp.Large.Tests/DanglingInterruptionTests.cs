using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

[Trait("Size", "Large")]
public sealed class DanglingInterruptionTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData(false, null, "BP", 1)]
    [InlineData(true, null, "BP", 1)]
    [InlineData(true, "BPIN", "BPIN", 1)]
    [InlineData(true, "BPOUT", "BPOUT", 3)]
    [InlineData(true, "BPF", "BPF", 1)]
    public async Task Interruption_WithOrWithoutCounterpartDevice_GraphicsAndLogicalReadback_SingleUndoRestores(
        bool counterpartDevice, string? symbolName, string expectedSymbol, int expectedVariant)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(2);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = session.Project, Page = pages[0] }, TestContext.Current.CancellationToken);
        double grid = geometry.GridSize;
        string suffix = pages[0].Split('/')[^1], name = "=GAA+A1-D5LINK" + suffix;
        double[] pin = [25 * grid, 40 * grid], end = [25 * grid, 46 * grid];
        double[] peerPin = [45 * grid, 40 * grid], peerEnd = [45 * grid, 46 * grid];
        ListData<SymbolSearchItem> symbols = await bridge.InvokeAsync<SymbolSearchArgs, ListData<SymbolSearchItem>>(OperationNames.SymbolSearch,
            new() { Project = session.Project, Library = "IEC_symbol", NameContains = "BAT", Limit = 200 }, TestContext.Current.CancellationToken);
        Assert.Equal(symbols.Total, symbols.Items.Count);
        SymbolSearchItem symbol = symbols.Items.Where(s => s.Name == "BAT" && s.ConnectionPoints.Any(p => p.Direction == "Up")
            && s.ConnectionPoints.All(p => p.Location.All(v => Math.Abs(v) <= 2 * grid)))
            .OrderBy(s => s.Variant).First();
        SymbolConnectionPoint offset = symbol.ConnectionPoints.First(p => p.Direction == "Up");
        async Task<string> PlaceDevice(string page, string tag, double[] position)
        {
            JsonElement result = await host.CallAsync("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["library"] = symbol.Library,
                ["symbol"] = symbol.Name,
                ["symbol_variant"] = symbol.Variant,
                ["tag"] = tag,
                ["x"] = position[0] - offset.Location[0],
                ["y"] = position[1] - offset.Location[1],
            });
            return result.GetProperty("device").GetProperty("tag").GetString()!;
        }
        string device = await PlaceDevice(pages[0], "-D5A" + suffix, pin);
        string? peerDevice = counterpartDevice ? await PlaceDevice(pages[1], "-D5B" + suffix, peerPin) : null;
        Dictionary<string, object?> Request(int index)
        {
            var item = new Dictionary<string, object?>
            {
                ["kind"] = "interruption_point",
                ["name"] = name,
                ["position"] = index == 0 ? end : peerEnd,
                ["direction"] = "Down",
                ["counterpart_pages"] = new[] { pages[1 - index] },
            };
            if (symbolName is not null)
            { item["symbol"] = symbolName; }
            return new()
            {
                ["project"] = session.Project,
                ["page"] = pages[index],
                ["response_format"] = "detailed",
                ["items"] = new[] { item },
            };
        }
        JsonElement peer = await host.CallAsync("eplan_connection_create", Request(1));
        Assert.Equal(1, peer.GetProperty("undoSteps").GetInt32());
        Assert.Empty(peer.GetProperty("actualEndpoints").EnumerateArray());
        // This older write must survive undoing the tested call.
        string sentinel = Assert.Single(await session.CreatePagesAsync(1));
        string before = (await session.SnapshotAsync()).Hash;
        JsonElement preview = await host.CallAsync("eplan_connection_create", new(Request(0)) { ["dry_run"] = true });
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build the connection helper before running D5 Large tests.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            JsonElement result = await host.CallAsync("eplan_connection_create", Request(0));
            TestContext.Current.TestOutputHelper!.WriteLine("D5 tool result: " + result);
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(1, result.GetProperty("undoSteps").GetInt32());
            JsonElement[] points = result.GetProperty("interruptionPoints").EnumerateArray().ToArray();
            Assert.Equal(2, points.Length);
            foreach (JsonElement point in points)
            {
                Assert.Equal(name, point.GetProperty("name").GetString());
                Assert.Equal("Down", point.GetProperty("direction").GetString());
                Assert.Equal(point.GetProperty("page").GetString() == pages[0] ? end : peerEnd,
                    point.GetProperty("pin").EnumerateArray().Select(n => n.GetDouble()));
                JsonElement counterpart = Assert.Single(point.GetProperty("counterparts").EnumerateArray());
                Assert.Contains(points, p => p.GetProperty("oid").GetInt64() == counterpart.GetProperty("oid").GetInt64());
            }
            JsonElement observed = await ObserveAsync(session.Project, pages);
            Assert.Equal(counterpartDevice ? 2 : 1, observed.GetProperty("devices").GetArrayLength());
            Assert.Equal(2, observed.GetProperty("interruptions").GetArrayLength());
            Assert.Contains(observed.GetProperty("segments").EnumerateArray(), s => s.GetProperty("page").GetString() == pages[0]
                && Segment(s.GetProperty("start"), s.GetProperty("end"), pin, end));
            foreach (JsonElement point in observed.GetProperty("interruptions").EnumerateArray())
            {
                Assert.Equal(name, point.GetProperty("name").GetString());
                Assert.Equal("Down", point.GetProperty("direction").GetString());
                Assert.Equal(expectedSymbol, point.GetProperty("symbol").GetString());
                Assert.Equal(expectedVariant, point.GetProperty("variant").GetInt32());
                bool targetPage = point.GetProperty("page").GetString() == pages[0];
                Assert.Equal(targetPage || counterpartDevice, point.GetProperty("connected").GetBoolean());
                Assert.Equal(targetPage ? end : peerEnd, point.GetProperty("pin").EnumerateArray().Select(n => n.GetDouble()));
                Assert.Single(point.GetProperty("counterpartsAll").EnumerateArray());
                JsonElement expected = Assert.Single(points, p => p.GetProperty("oid").GetInt64() == point.GetProperty("oid").GetInt64());
                Assert.Equal(expected.GetProperty("position").EnumerateArray().Select(n => n.GetDouble()), point.GetProperty("position").EnumerateArray().Select(n => n.GetDouble()));
            }
            JsonElement[] connections = observed.GetProperty("connections").EnumerateArray().ToArray();
            if (counterpartDevice)
            {
                JsonElement connection = Assert.Single(connections);
                Assert.Equal(new[] { device, peerDevice }.Order(StringComparer.Ordinal),
                    new[] { connection.GetProperty("from").GetProperty("name").GetString(), connection.GetProperty("to").GetProperty("name").GetString() }.Order(StringComparer.Ordinal));
                Assert.Contains(observed.GetProperty("segments").EnumerateArray(), s => s.GetProperty("page").GetString() == pages[1]
                    && Segment(s.GetProperty("start"), s.GetProperty("end"), peerPin, peerEnd));
                Assert.NotEmpty(result.GetProperty("actualEndpoints").EnumerateArray());
            }
            else
            {
                Assert.Empty(connections);
                Assert.Empty(result.GetProperty("actualEndpoints").EnumerateArray());
            }
            JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = pages, ["format"] = "png" });
            TestContext.Current.TestOutputHelper!.WriteLine("D5 EPLAN images: " + image);
            Assert.Equal(2, image.GetProperty("fileCount").GetInt32());
            await session.UndoAsync();
            JsonElement restored = await host.CallAsync("eplan_connection_create", new(Request(0)) { ["dry_run"] = true });
            Assert.Equal(preview.GetProperty("fingerprint").GetString(), restored.GetProperty("fingerprint").GetString());
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            JsonElement after = await ObserveAsync(session.Project, pages);
            Assert.Single(after.GetProperty("interruptions").EnumerateArray());
            Assert.DoesNotContain(after.GetProperty("segments").EnumerateArray(), s => s.GetProperty("page").GetString() == pages[0]);
            await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = sentinel });
        }
        finally
        { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
    }

    private async Task<JsonElement> ObserveAsync(string project, string[] pages)
    {
        string file = Path.Combine(host.Home, "d5-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { project, pages, mode = "read" }), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSpike /REQUEST:\"" + file + "\"");
        string text = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine("D5 EPLAN native graphics/names/positions/connections: " + text);
        using JsonDocument result = JsonDocument.Parse(text);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
        return result.RootElement.GetProperty("observation").Clone();
    }

    private static bool Segment(JsonElement start, JsonElement finish, double[] a, double[] b) =>
        start.EnumerateArray().Select(n => n.GetDouble()).SequenceEqual(a) && finish.EnumerateArray().Select(n => n.GetDouble()).SequenceEqual(b)
        || start.EnumerateArray().Select(n => n.GetDouble()).SequenceEqual(b) && finish.EnumerateArray().Select(n => n.GetDouble()).SequenceEqual(a);
}
