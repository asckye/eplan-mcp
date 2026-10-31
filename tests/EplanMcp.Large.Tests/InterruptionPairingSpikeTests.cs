using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/*
实验记录草稿（D2；尚未实机运行，不是 API 结论）：
1. 对照 same-page / two-pages、forward / reverse 的 setup 输出与 observation.pagesInProjectOrder；
   setup 给出 A/B/C/D、实际创建次序、页、位置、方向、独立器件和初始名称，排除名称相撞。
2. 逐阶段看 two-same-name、three-same-name、four-same-name 的 interruptions：
   按 oid 对照 name、position、direction、starSource 和 counterpartsAll，记录第三点是否改变原配对；
   All 可能只表示同名引用组，须经 attachedDevices 对照 connections 的 from/to（器件、引脚、页）判断电气配对。
3. 看 interruptions.properties 的 24300、24301、24351、24000 与 displayText 的实际显示文字；
   同时记录 24809（排序码）、24020/24021（显示选项），保留空值与 readError，不能由空文字推断未配对。
4. 比较同页位置、跨页顺序及反向创建时的配对和逻辑端点；记录生成失败/error，实测后再填写规则及局限。
*/
[Trait("Size", "Large")]
[Trait("Spike", "D2")]
public sealed class InterruptionPairingSpikeTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SameNamedPoints_OnOneOrTwoPages_PrintsPairingAsThirdAndFourthJoin(bool samePage, bool reverseCreation)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string[] pages = await session.CreatePagesAsync(samePage ? 1 : 2);
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        PageDrawingGeometry geometry = await bridge.InvokeAsync<PageGetArgs, PageDrawingGeometry>(OperationNames.PageDrawingGet,
            new() { Project = session.Project, Page = pages[0] }, TestContext.Current.CancellationToken);
        double grid = geometry.GridSize;
        ListData<SymbolSearchItem> symbols = await bridge.InvokeAsync<SymbolSearchArgs, ListData<SymbolSearchItem>>(OperationNames.SymbolSearch,
            new() { Project = session.Project, Library = "IEC_symbol", NameContains = "BAT", Limit = 200 }, TestContext.Current.CancellationToken);
        Assert.Equal(symbols.Total, symbols.Items.Count);
        string suffix = pages[0].Split('/')[^1];
        string sharedName = "=GAA+A1-I2D2" + suffix;
        int[] order = reverseCreation ? [3, 2, 1, 0] : [0, 1, 2, 3];
        var points = Enumerable.Range(0, 4).Select(i => new
        {
            label = ((char)('A' + i)).ToString(),
            page = pages[samePage ? 0 : i % 2],
            position = new[] { (25 + 20 * i) * grid, (i < 2 ? 50 : 30) * grid },
            direction = i % 2 == 0 ? "Up" : "Down",
            initialName = FormattableString.Invariant($"=GAA+A1-D2{suffix}P{i}"),
            device = FormattableString.Invariant($"-D2{suffix}B{i}"),
        }).ToArray();
        string scenario = (samePage ? "same-page" : "two-pages") + (reverseCreation ? " reverse" : " forward");
        TestContext.Current.TestOutputHelper!.WriteLine($"D2 {scenario} setup: sharedName={sharedName}; creationOrder={JsonSerializer.Serialize(order)}; points={JsonSerializer.Serialize(points)}");
        foreach (int i in order)
        {
            var point = points[i];
            double[] pin = [point.position[0], point.position[1] + (point.direction == "Up" ? 8 : -8) * grid];
            await PlacePinAsync(session.Project, point.page, symbols.Items, grid, point.device, pin, point.direction == "Up" ? "Down" : "Up");
            // The product deliberately refuses duplicate same-page names. Place isolated names through it first,
            // then change only these known point OIDs in the test-only native helper.
            JsonElement placed = await host.CallAsync("eplan_connection_create", new()
            {
                ["project"] = session.Project,
                ["page"] = point.page,
                ["response_format"] = "detailed",
                ["items"] = JsonSerializer.SerializeToElement(new[] { new { kind = "interruption_point", name = point.initialName,
                    position = point.position, direction = point.direction, counterpart_pages = pages } }),
            });
            Assert.Equal(ExecutionState.Committed, placed.GetProperty("execution").GetString());
        }
        foreach (string page in pages)
        {
            JsonElement setup = await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page });
            Assert.Equal(samePage ? 4 : 2, setup.GetProperty("deviceCount").GetInt32());
        }
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        Assert.True(File.Exists(helper), "Build tests/EplanMcp.Large.ConnectionHelper before running D2.");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        try
        {
            JsonElement initial = await ProbeAsync(new { project = session.Project, pages, mode = "read" }, "D2 " + scenario + " isolated");
            JsonElement[] inventory = initial.GetProperty("observation").GetProperty("interruptions").EnumerateArray().ToArray();
            Assert.Equal(4, inventory.Length);
            long[] ids = points.Select(p => Assert.Single(inventory, q => q.GetProperty("name").GetString() == p.initialName
                && q.GetProperty("page").GetString() == p.page).GetProperty("oid").GetInt64()).ToArray();
            foreach (int count in new[] { 2, 3, 4 })
            {
                string stage = count switch { 2 => "two-same-name", 3 => "three-same-name", _ => "four-same-name" };
                JsonElement renamed = await ProbeAsync(new { project = session.Project, pages, mode = "rename", name = sharedName, oids = ids.Take(count).ToArray() },
                    "D2 " + scenario + " " + stage + " rename-committed");
                Assert.True(renamed.GetProperty("success").GetBoolean(), "Same-name setup failed; inspect the printed native error.");
                var generated = await eplan.CallOrErrorAsync("eplan_connection_generate", new() { ["project"] = session.Project, ["pages"] = pages });
                TestContext.Current.TestOutputHelper!.WriteLine($"D2 {scenario} {stage} generate: {generated.Result}");
                await ProbeAsync(new { project = session.Project, pages, mode = "read" }, "D2 " + scenario + " " + stage);
                if (!generated.Succeeded)
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
    }

    private async Task<JsonElement> ProbeAsync(object request, string stage)
    {
        string file = Path.Combine(host.Home, "d2-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(request), TestContext.Current.CancellationToken);
        await PlanStepLiveSession.ActionAsync("EplanMcpLargeConnectionSpike /REQUEST:\"" + file + "\"");
        Assert.True(File.Exists(file + ".result.json"), "The native probe did not write a response.");
        string text = await File.ReadAllTextAsync(file + ".result.json", TestContext.Current.CancellationToken);
        TestContext.Current.TestOutputHelper!.WriteLine(stage + ": " + text);
        using JsonDocument result = JsonDocument.Parse(text);
        return result.RootElement.Clone();
    }
}
