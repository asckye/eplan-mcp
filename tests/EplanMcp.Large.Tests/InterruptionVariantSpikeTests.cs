using System.Text.Json;

namespace EplanMcp.Large.Tests;

/*
实验记录（D7，2026-11-02 实机，结论见 api-notes A81）：
客户页的中断点是红色实心箭头；本探测在一页上放出 SPECIAL/BPF 的每个变体（名称 D7V<变体>，按变体的连接点方向请求），
打印连接点和属性文字，并把页面图片复制到 %TEMP%\eplanmcp-spikes。结果：BPF 只有 4 个变体，每个方向一个，
都画成一段短线、没有箭头，所以箭头要从其它符号库选，不是换变体。
*/
[Trait("Size", "Large")]
[Trait("Spike", "D7")]
public sealed class InterruptionVariantSpikeTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    [Fact]
    public async Task EachSpecialInterruptionSymbol_OnOnePage_PrintsTextsAndExportsTheImage()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        // EPLAN's insert command (Shift+F4) may use another SPECIAL interruption symbol than BPF; list every one.
        // The whole SPECIAL list also shows the strip definition symbol EPLAN's insert command uses (D10).
        var all = new List<JsonElement>();
        string? cursor = null;
        do
        {
            JsonElement found = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = session.Project, ["library"] = "SPECIAL", ["limit"] = 200, ["cursor"] = cursor });
            all.AddRange(found.GetProperty("symbols").EnumerateArray());
            cursor = found.GetProperty("nextCursor").GetString();
        } while (cursor is not null);
        string evidence = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        await File.WriteAllTextAsync(Path.Combine(evidence, "D7-special-symbols.json"), JsonSerializer.Serialize(all), TestContext.Current.CancellationToken);
        List<JsonElement> variants = [.. all.Where(s => s.GetProperty("type").GetString() == "InterruptionPoint")];
        Assert.NotEmpty(variants);
        string page = (await session.CreatePagesAsync(1))[0];
        var output = TestContext.Current.TestOutputHelper!;
        for (int i = 0; i < variants.Count; i++)
        {
            int variant = variants[i].GetProperty("variant").GetInt32();
            string symbol = variants[i].GetProperty("name").GetString()!;
            JsonElement[] pins = [.. variants[i].GetProperty("connectionPoints").EnumerateArray()];
            output.WriteLine($"D7 {symbol} variant {variant} ({variants[i].GetProperty("description").GetString()}): {JsonSerializer.Serialize(pins)}");
            // Live 2026-11-02: one SPECIAL interruption symbol has two pins (Right, Left); connection_create places one-pin points.
            if (pins.Length != 1)
            { continue; }
            JsonElement pin = pins[0];
            // Columns 40 mm apart, two rows, away from the frame.
            double[] position = [60 + 40 * (i % 8), 240 - 40 * (i / 8)];
            string outcome = "placed";
            try
            {
                await host.CallAsync("eplan_connection_create", new()
                {
                    ["project"] = session.Project,
                    ["page"] = page,
                    ["items"] = JsonSerializer.SerializeToElement(new[] { new { kind = "interruption_point", name = "=GAA+A1-D7" + symbol + variant, symbol, position, direction = pin.GetProperty("direction").GetString(), counterpart_pages = new[] { page } } }),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { outcome = ex.Message; }
            output.WriteLine($"D7 {symbol} variant {variant} at {JsonSerializer.Serialize(position)}: {outcome}");
        }
        JsonElement drawings = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" })).GetProperty("drawings");
        foreach (JsonElement item in drawings.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("kind").GetString() is "InterruptionPoint" or "PropertyPlacement"))
        { output.WriteLine("D7 drawing: " + item.GetRawText()); }
        JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = new[] { page }, ["format"] = "png", ["width_px"] = 2400 });
        string root = image.GetProperty("directory").GetString()!;
        foreach (JsonElement file in image.GetProperty("files").EnumerateArray())
        {
            string target = Path.Combine(evidence, "D7-interruption-symbols.png");
            File.Copy(Path.Combine(root, file.GetProperty("path").GetString()!), target, overwrite: true);
            output.WriteLine("D7 image: " + target);
        }
    }
}
