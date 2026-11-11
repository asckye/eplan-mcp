#pragma warning disable CA1861 // One-off spike: literal arrays read better than shared fields.
using System.Globalization;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.Tests;

/// <summary>One-off, never lands: draws the README demo page on a new scratch project, only through MCP tools.</summary>
[Trait("Size", "Large")]
[Trait("Spike", "ReadmeDemo")]
public sealed class ReadmeDemoTests(LiveEplan eplan)
{
    private const string Page = "=GAA+A1&EFS1/1";
    private readonly JObject _evidence = new();
    private string _project = "";
    private int _step;

    private static string Output => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "eplanmcp-spikes")).FullName;

    private async Task CreateProjectAsync()
    {
        string run = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string name = "ReadmeDemo_" + run;
        _project = Path.Combine(WriteHost.ScratchFolder, "readme-demo", name, name);
        string basic = ProjectCreateTests.Find("IEC_bas003.zw9") ?? throw new InvalidOperationException("IEC_bas003.zw9 was not found.");
        string relative = "readme-demo/" + run + "/IEC_bas003.zw9";
        string imported = Path.Combine(LiveEplan.DataRoot(), "imports", relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        File.Copy(basic, imported);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(_project))!);
        JsonElement created = await eplan.CallAsync("eplan_project_create", new()
        {
            ["mode"] = "basic_project",
            ["file"] = relative,
            ["name"] = name,
            ["destination"] = Path.GetDirectoryName(_project),
            ["dry_run"] = false,
        });
        await PlanStepLiveSession.ActionAsync("ProjectOpen /Project:\"" + created.GetProperty("project").GetProperty("path").GetString() + "\" /Quiet");
    }

    private async Task<JToken> StepAsync(string label, string tool, Dictionary<string, object?> args)
    {
        args["project"] = _project;
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync(tool, args);
        JToken body = ok ? JToken.Parse(result.GetRawText()) : new JObject { ["error"] = result.GetRawText() };
        _evidence[(++_step).ToString("D3", CultureInfo.InvariantCulture) + " " + label] = ok && tool != "eplan_symbol_search" && !tool.Contains("_get", StringComparison.Ordinal) && tool != "eplan_export_image"
            ? "ok" : body;
        return body;
    }

    private Task<JToken> SymbolAsync(string tag, string symbol, double x, double y, bool join = false, string[]? pins = null)
    {
        var args = new Dictionary<string, object?>
        {
            ["page"] = Page,
            ["library"] = "IEC_symbol",
            ["symbol"] = symbol,
            ["symbol_variant"] = 0,
            ["tag"] = tag,
            ["x"] = x,
            ["y"] = y,
            ["layout_guard"] = "warn",
            ["dry_run"] = false,
        };
        if (join)
        { args["join_device"] = true; }
        if (pins is not null)
        { args["connection_designations"] = pins; }
        return StepAsync(tag + " " + symbol, "eplan_device_place", args);
    }

    [Fact]
    public async Task Draw()
    {
        await CreateProjectAsync();
        var names = new JObject();
        foreach (string letter in new[] { "H", "K", "M", "O", "S", "W" })
        {
            string? cursor = null;
            for (int round = 0; round < 12; round++)
            {
                var args = new Dictionary<string, object?> { ["project"] = _project, ["library"] = "IEC_symbol", ["kind"] = "function", ["name_contains"] = letter, ["language"] = "zh_CN", ["limit"] = 200 };
                if (cursor is not null)
                { args["cursor"] = cursor; }
                (bool ok, JsonElement page) = await eplan.CallOrErrorAsync("eplan_symbol_search", args);
                if (!ok)
                { break; }
                foreach (JsonElement row in page.GetProperty("symbols").EnumerateArray())
                {
                    string name = row.GetProperty("name").GetString()!;
                    if (name.StartsWith(letter, StringComparison.Ordinal))
                    { names[name] ??= row.GetProperty("description").GetString(); }
                }
                cursor = page.TryGetProperty("nextCursor", out JsonElement next) ? next.GetString() : null;
                if (cursor is null)
                { break; }
            }
        }
        _evidence["names"] = names;
        await StepAsync("page", "eplan_page_create", new()
        {
            ["pages"] = new object[] { new Dictionary<string, object?> { ["name"] = Page, ["description"] = "电动机启动回路" } },
            ["language"] = "zh_CN",
            ["dry_run"] = false,
        });

        await StepAsync("open", "eplan_page_open", new() { ["page"] = Page });

        // Power circuit: every phase runs straight down, so EPLAN's autoconnect wires it.
        await StepAsync("incoming", "eplan_connection_create", new()
        {
            ["page"] = Page,
            ["generate"] = false,
            ["layout_guard"] = "warn",
            ["dry_run"] = false,
            ["items"] = new object[]
            {
                new { kind = "interruption_point", name = "=GAA-L1", direction = "Down", position = new[] { 60, 196 } },
                new { kind = "interruption_point", name = "=GAA-L2", direction = "Down", position = new[] { 68, 196 } },
                new { kind = "interruption_point", name = "=GAA-L3", direction = "Down", position = new[] { 76, 196 } },
                new { kind = "interruption_point", name = "=GAA-PE", direction = "Up", position = new[] { 76, 68 } },
                new { kind = "interruption_point", name = "=GAA-24V", direction = "Down", position = new[] { 200, 196 } },
                new { kind = "interruption_point", name = "=GAA-0V", direction = "Up", position = new[] { 200, 52 } },
            },
        });
        await StepAsync("-Q1 part", "eplan_device_place", new() { ["page"] = Page, ["part"] = "SIE.3RV2011-1GA15", ["tag"] = "-Q1", ["x"] = 60, ["y"] = 168, ["layout_guard"] = "warn", ["dry_run"] = false });

        // Control circuit, then the contacts of the same device: the engineer's order.
        await SymbolAsync("-S1", "SOA", 200, 168);
        await SymbolAsync("-S2", "SSA", 200, 128);
        await SymbolAsync("-K1", "K", 200, 84);
        await SymbolAsync("-H1", "H", 232, 84);
        await SymbolAsync("-K1", "S", 60, 128, true, ["1/L1", "2/T1"]);
        await SymbolAsync("-K1", "S", 68, 128, true, ["3/L2", "4/T2"]);
        await SymbolAsync("-K1", "S", 76, 128, true, ["5/L3", "6/T3"]);
        await SymbolAsync("-K1", "S", 216, 128, true, ["13", "14"]);
        await SymbolAsync("-M1", "M3", 60, 92);
        await StepAsync("nodes", "eplan_connection_create", new()
        {
            ["page"] = Page,
            ["generate"] = false,
            ["layout_guard"] = "warn",
            ["dry_run"] = false,
            ["items"] = new object[]
            {
                new { kind = "t_node", position = new[] { 200, 148 }, directions = new[] { "Up", "Down", "Right" } },
                new { kind = "corner", position = new[] { 216, 148 }, directions = new[] { "Left", "Down" } },
                new { kind = "t_node", position = new[] { 200, 112 }, directions = new[] { "Up", "Down", "Right" } },
                new { kind = "t_node", position = new[] { 216, 112 }, directions = new[] { "Left", "Up", "Right" } },
                new { kind = "corner", position = new[] { 232, 112 }, directions = new[] { "Left", "Down" } },
                new { kind = "t_node", position = new[] { 200, 68 }, directions = new[] { "Up", "Down", "Right" } },
                new { kind = "corner", position = new[] { 232, 68 }, directions = new[] { "Left", "Up" } },
            },
        });
        await StepAsync("generate", "eplan_connection_generate", new() { ["pages"] = new[] { Page }, ["dry_run"] = false });
        await StepAsync("labels", "eplan_placement_create", new()
        {
            ["page"] = Page,
            ["layout_guard"] = "warn",
            ["dry_run"] = false,
            ["items"] = new object[]
            {
                new { kind = "text", position = new[] { 44, 212 }, height_mm = 4.0, contents = new Dictionary<string, string> { ["??_??"] = "主回路 Main circuit" } },
                new { kind = "text", position = new[] { 184, 212 }, height_mm = 4.0, contents = new Dictionary<string, string> { ["??_??"] = "控制回路 Control circuit" } },
            },
        });
        await StepAsync("page read", "eplan_page_get", new() { ["page"] = Page, ["include_drawings"] = true, ["drawing_limit"] = 200 });
        await StepAsync("image", "eplan_export_image", new() { ["pages"] = new[] { Page }, ["format"] = "png", ["width_px"] = 3000 });
        File.WriteAllText(Path.Combine(Output, "readme-demo-draw.json"), _evidence.ToString());
    }
}
