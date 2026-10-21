using System.Diagnostics;
using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// The terminal strip tools against the fixture's known data (api-notes A24, S21: 45 strips, +A1-XD4 with 15
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
        Assert.Equal(45, data.GetProperty("total").GetInt32());
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

    private static JsonElement Strip(JsonElement[] strips, string name) => strips.Single(s => s.GetProperty("name").GetString() == name);
}
