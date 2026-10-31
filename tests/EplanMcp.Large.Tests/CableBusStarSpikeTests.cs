using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/*
实验记录草稿（D1；尚未实机运行，不是 API 结论）：
1. 对照 before-node / after-node 的 setup、generated、defined、bound、templates、assigned 输出；
   setup 给出三器件引脚、节点和定义线坐标，observation.segments 给出去重图形段。
2. 看 observation.connections 的 oid、from/to（器件、引脚）、drawnPath 和 definitionPoints；
   对照 cables.crossedSegments、memberOids，记录同一图形段是否属于多条器件间逻辑连接。
3. 看 cables.templates（固定独立输入 1/RD、2/BK、3/BU）及各连接 properties 的
   20005（电缆）、31011（芯号）、31004（颜色/编号）、31002（截面）；比较分配前后，
   按 memberOids 统计实际分配数，判别按逻辑连接还是按图形段分芯，并记录 RD 去向及空值。
4. 任一阶段 error、生成返回值和读取异常也要记录；不能把空值或未完成分配写成已验证规则。
*/
[Trait("Size", "Large")]
[Trait("Spike", "D1")]
public sealed class CableBusStarSpikeTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly string[] _starDirections = ["Up", "Down", "Right"];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CableCrossingBusStar_BeforeOrAfterNode_PrintsNativeCoreAssignment(bool beforeNode)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(2);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = session.Project, Page = pages[0] }, TestContext.Current.CancellationToken);
        double grid = geometry.GridSize;
        ListData<SymbolSearchItem> symbols = await bridge.InvokeAsync<SymbolSearchArgs, ListData<SymbolSearchItem>>(OperationNames.SymbolSearch,
            new() { Project = session.Project, Library = "IEC_symbol", NameContains = "BAT", Limit = 200 }, TestContext.Current.CancellationToken);
        Assert.Equal(symbols.Total, symbols.Items.Count);
        string suffix = pages[0].Split('/')[^1];
        await PlacePinAsync(session.Project, pages[0], symbols.Items, grid, "-D1" + suffix + "A", [30 * grid, 55 * grid], "Down");
        await PlacePinAsync(session.Project, pages[0], symbols.Items, grid, "-D1" + suffix + "B", [30 * grid, 25 * grid], "Up");
        await PlacePinAsync(session.Project, pages[0], symbols.Items, grid, "-D1" + suffix + "C", [50 * grid, 40 * grid], "Left");
        // A55: first generate the initial A-B autoconnect; the product then splits it with one T and adds C.
        await host.CallAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { pages[0] } });
        JsonElement node = await host.CallAsync("eplan_connection_create", new()
        {
            ["project"] = session.Project,
            ["page"] = pages[0],
            ["response_format"] = "detailed",
            ["items"] = JsonSerializer.SerializeToElement(new[] { new { kind = "t_node", position = new[] { 30 * grid, 40 * grid }, directions = _starDirections } }),
        });
        Assert.Equal(ExecutionState.Committed, node.GetProperty("execution").GetString());
        JsonElement setup = await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = pages[0], ["include_drawings"] = true });
        Assert.Equal(3, setup.GetProperty("deviceCount").GetInt32());

        // These three independent wires supply a fixed template pool, never a guessed mapping onto the star.
        for (int i = 0; i < 3; i++)
        {
            await PlacePinAsync(session.Project, pages[1], symbols.Items, grid, FormattableString.Invariant($"-D1{suffix}S{i}A"), [(20 + 15 * i) * grid, 45 * grid], "Down");
            await PlacePinAsync(session.Project, pages[1], symbols.Items, grid, FormattableString.Invariant($"-D1{suffix}S{i}B"), [(20 + 15 * i) * grid, 25 * grid], "Up");
        }
        await host.CallAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { pages[1] } });
        JsonElement seeds = await eplan.CallAsync("eplan_connection_list", new() { ["project"] = session.Project, ["page"] = pages[1] });
        Assert.Equal(3, seeds.GetProperty("connections").GetArrayLength());
        string side = beforeNode ? "before-node" : "after-node";
        double y = (beforeNode ? 47 : 33) * grid;
        double[][] line = [[20 * grid, y], [40 * grid, y]];
        string cable = "=GAA+A1-WD1" + suffix;
        TestContext.Current.TestOutputHelper!.WriteLine($"D1 {side} setup: starPage={pages[0]}, seedPage={pages[1]}, grid={grid.ToString(CultureInfo.InvariantCulture)}, line={JsonSerializer.Serialize(line)}; node={node}");

        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running D1.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            async Task<JsonElement> Probe(string mode, string stage) => await ProbeAsync(new
            { project = session.Project, pages, mode, cable, line }, "D1 " + side + " " + stage);
            await Probe("read", "generated");
            JsonElement defined = await Probe("define", "definition-committed");
            Assert.True(defined.GetProperty("success").GetBoolean(), "Cable definition setup failed; inspect the printed native error.");
            foreach ((string mode, string stage) in new[] { ("read", "defined"), ("bind", "binding-committed"), ("read", "bound"), ("templates", "templates"), ("assign", "assignment-committed"), ("read", "assigned") })
            {
                if (mode == "read")
                {
                    var generated = await eplan.CallOrErrorAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = new[] { pages[0] } });
                    TestContext.Current.TestOutputHelper!.WriteLine($"D1 {side} generate before {stage}: {generated.Result}");
                    if (!generated.Succeeded)
                    { return; }
                }
                JsonElement result = await Probe(mode, stage);
                if (!result.GetProperty("success").GetBoolean() || result.TryGetProperty("error", out _))
                { return; }
            }
        }
        finally
        {
            await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet");
        }
    }

    private async Task PlacePinAsync(string project, string page, List<SymbolSearchItem> symbols, double grid, string tag, double[] pin, string direction)
    {
        SymbolSearchItem? symbol = symbols.Where(s => s.Name == "BAT" && s.Type == "Function" && s.ConnectionPoints.Count == 2
            && s.ConnectionPoints.Any(p => p.Direction == direction)
            && s.ConnectionPoints.All(p => p.Location.All(v => Math.Abs(v) <= 2 * grid && Math.Abs(v / grid - Math.Round(v / grid)) < 1e-8)))
            .OrderBy(s => s.Variant).FirstOrDefault();
        Assert.NotNull(symbol);
        SymbolConnectionPoint point = symbol.ConnectionPoints.First(p => p.Direction == direction);
        JsonElement placed = await host.CallAsync("eplan_device_place", new()
        {
            ["project"] = project,
            ["page"] = page,
            ["library"] = symbol.Library,
            ["symbol"] = symbol.Name,
            ["symbol_variant"] = symbol.Variant,
            ["tag"] = tag,
            ["x"] = pin[0] - point.Location[0],
            ["y"] = pin[1] - point.Location[1],
        });
        Assert.Equal(ExecutionState.Committed, placed.GetProperty("execution").GetString());
        TestContext.Current.TestOutputHelper!.WriteLine($"D1 setup device {tag}: pin={JsonSerializer.Serialize(pin)}, direction={direction}, placed={placed}");
    }

    private async Task<JsonElement> ProbeAsync(object request, string stage)
    {
        string file = Path.Combine(host.Home, "d1-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSpike /REQUEST:\"" + file + "\"");
        Assert.True(File.Exists(file + ".result.json"), "The native probe did not write a response.");
        string text = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine(stage + ": " + text);
        using JsonDocument result = JsonDocument.Parse(text);
        return result.RootElement.Clone();
    }
}
