using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The terminal strip tools against the fixture's known data (api-notes A24, S21: 45 strip objects, 27 strips, +A1-XD4 with 15
/// terminals, +A2-XD2 with 3 bridges). The time S21 did not measure goes to the test output.
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class TerminalTests(LiveEplan eplan)
{
    [Fact]
    public async Task TerminalStripList_ListsTheSampleStripsSortedWithTheirCounts()
    {
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_terminal_strip_list", new() { ["limit"] = 200 });
        clock.Stop();

        JsonElement[] strips = [.. data.GetProperty("strips").EnumerateArray()];
        string[] names = [.. strips.Select(s => s.GetProperty("name").GetString()!)];
        // GetTerminalStrips returns 45 objects for these 27 strips; 18 come twice (tool spec 5).
        Assert.Equal(27, data.GetProperty("total").GetInt32());
        Assert.Equal(names.Distinct(StringComparer.Ordinal).Count(), names.Length);
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(15, Strip(strips, "+A1-XD4").GetProperty("terminals").GetInt32());
        Assert.Equal(3, Strip(strips, "+A2-XD2").GetProperty("bridges").GetInt32());
        TestContext.Current.TestOutputHelper?.WriteLine($"Listed {names.Length} strips with {strips.Sum(s => s.GetProperty("terminals").GetInt32())} terminals in {clock.ElapsedMilliseconds} ms (S21: 1331 terminals in the project).");
    }

    [Fact]
    public async Task TerminalStripList_NameContains_IgnoresCase()
    {
        JsonElement data = await eplan.CallAsync("eplan_terminal_strip_list", new() { ["name_contains"] = "+a2-xd2" });

        Assert.Contains("+A2-XD2", data.GetProperty("strips").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task TerminalStripGet_ReadsTheBridgedSampleStrip()
    {
        JsonElement data = await eplan.CallAsync("eplan_terminal_strip_get", new() { ["strip"] = "+A2-XD2" });

        JsonElement[] terminals = [.. data.GetProperty("terminals").EnumerateArray()];
        Assert.Equal((5, 3), (data.GetProperty("strip").GetProperty("terminals").GetInt32(), data.GetProperty("strip").GetProperty("bridges").GetInt32()));
        Assert.Equal(5, terminals.Length);
        Assert.All(terminals, t => Assert.StartsWith("+A2-XD2:", t.GetProperty("name").GetString(), StringComparison.Ordinal));
        Assert.Contains(terminals, t => t.GetProperty("bridgedWith").GetArrayLength() > 0);

        // Unverified by S21 (tool spec 5): order, sides, bridges and PLC; compare them with the terminal diagram in EPLAN.
        foreach (JsonElement t in terminals)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{t.GetProperty("name")} main {t.GetProperty("main")} page {t.GetProperty("page")} inside {t.GetProperty("inside")} outside {t.GetProperty("outside")} bridged {t.GetProperty("bridgedWith")} plc {t.GetProperty("plc")}");
        }
    }

    [Fact]
    public async Task TerminalStripGet_TheLongestSampleStrip_IsReadInTime()
    {
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_terminal_strip_get", new() { ["strip"] = "+B2.X1-XZ1", ["limit"] = 200 });
        clock.Stop();

        Assert.Equal(33, data.GetProperty("total").GetInt32());
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"33 terminals in {clock.ElapsedMilliseconds} ms; with a PLC: {data.GetProperty("terminals").EnumerateArray().Count(t => t.GetProperty("plc").ValueKind == JsonValueKind.String)}.");
    }

    [Fact]
    public async Task TerminalStripGet_ShortNameAndUnknownName_FindTheStripOrSaySo()
    {
        (bool found, JsonElement result) = await eplan.CallOrErrorAsync("eplan_terminal_strip_get", new() { ["strip"] = "-XD4" });
        JsonElement unknown = await eplan.ErrorAsync("eplan_terminal_strip_get", new() { ["strip"] = "+A1-XD999" });

        // -XD4 may exist in several locations; then the full name +A1-XD4 must be among the candidates.
        Assert.True(found
            ? result.GetProperty("strip").GetProperty("name").GetString() == "+A1-XD4"
            : result.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n').Contains("+A1-XD4"));
        Assert.Equal(ErrorReason.DeviceNotFound, unknown.GetProperty("reason").GetString());
    }

    private static JsonElement Strip(JsonElement[] strips, string name) => strips.Single(s => s.GetProperty("name").GetString() == name);
}
