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
    public async Task EachBpfVariant_OnOnePage_PrintsTextsAndExportsTheImage()
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        JsonElement found = await eplan.CallAsync("eplan_symbol_search", new() { ["project"] = session.Project, ["library"] = "SPECIAL", ["name_contains"] = "BPF", ["limit"] = 200 });
        JsonElement[] variants = [.. found.GetProperty("symbols").EnumerateArray().Where(s => s.GetProperty("name").GetString() == "BPF").OrderBy(s => s.GetProperty("variant").GetInt32())];
        Assert.NotEmpty(variants);
        string page = (await session.CreatePagesAsync(1))[0];
        var output = TestContext.Current.TestOutputHelper!;
        for (int i = 0; i < variants.Length; i++)
        {
            int variant = variants[i].GetProperty("variant").GetInt32();
            JsonElement pin = Assert.Single(variants[i].GetProperty("connectionPoints").EnumerateArray());
            output.WriteLine($"D7 BPF variant {variant}: {pin.GetRawText()}");
            // Columns 40 mm apart, two rows, away from the frame.
            double[] position = [60 + 40 * (i % 8), 200 - 80 * (i / 8)];
            string outcome = "placed";
            try
            {
                await host.CallAsync("eplan_connection_create", new()
                {
                    ["project"] = session.Project,
                    ["page"] = page,
                    ["items"] = JsonSerializer.SerializeToElement(new[] { new { kind = "interruption_point", name = "=GAA+A1-D7V" + variant, position, direction = pin.GetProperty("direction").GetString(), counterpart_pages = new[] { page } } }),
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { outcome = ex.Message; }
            output.WriteLine($"D7 variant {variant} at {JsonSerializer.Serialize(position)}: {outcome}");
        }
        JsonElement drawings = (await eplan.CallAsync("eplan_page_get", new() { ["project"] = session.Project, ["page"] = page, ["include_drawings"] = true, ["drawing_limit"] = 200, ["response_format"] = "detailed" })).GetProperty("drawings");
        foreach (JsonElement item in drawings.GetProperty("items").EnumerateArray().Where(d => d.GetProperty("kind").GetString() is "InterruptionPoint" or "PropertyPlacement"))
        { output.WriteLine("D7 drawing: " + item.GetRawText()); }
        JsonElement image = await eplan.CallAsync("eplan_export_image", new() { ["project"] = session.Project, ["pages"] = new[] { page }, ["format"] = "png", ["width_px"] = 2400 });
        string evidence = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;
        string root = image.GetProperty("directory").GetString()!;
        foreach (JsonElement file in image.GetProperty("files").EnumerateArray())
        {
            string target = Path.Combine(evidence, "D7-bpf-variants.png");
            File.Copy(Path.Combine(root, file.GetProperty("path").GetString()!), target, overwrite: true);
            output.WriteLine("D7 image: " + target);
        }
    }
}
