using System.Text.Json;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Agent eval 2026-11-05: a model placed three symbols, then asked for two interruption points with generate:false, and
/// the tool answered OUTCOME_UNKNOWN twice. The same calls, on a new page of the write copy.
/// </summary>
[Trait("Size", "Large")]
public sealed class DeferredInterruptionTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private static readonly int[] _top = [100, 250];

    public static TheoryData<string, string, string?, int?> Requests => new()
    {
        { "Up", "Down", null, null },
        { "Down", "Up", "BP", 3 },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task ShortNames_AboveAndBelowPlacedSymbols_TakeThePageStructure_OneUndoRestores(string top, string bottom, string? symbol, int? variant)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string suffix = page[(page.LastIndexOf('/') + 1)..];
        await PlaceSymbolsAsync(session, page, suffix);
        string before = (await session.SnapshotAsync(page)).Hash;
        object Item(string name, int y, string direction) => symbol is null
            ? new { kind = "interruption_point", name, position = new[] { 100, y }, direction }
            : new { kind = "interruption_point", name, position = new[] { 100, y }, direction, symbol, symbol_variant = variant };
        (bool ok, JsonElement result) = await eplan.CallOrErrorAsync("eplan_connection_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["generate"] = false,
            ["dry_run"] = false,
            ["items"] = new[] { Item("24V" + suffix, 250, top), Item("-0V" + suffix, 130, bottom) },
        });
        TestContext.Current.TestOutputHelper?.WriteLine("result: " + result.GetRawText());
        Assert.True(ok, result.GetRawText());
        string structure = "=" + page[1..page.IndexOf('+', StringComparison.Ordinal)];
        Assert.Equal([structure + "-0V" + suffix, structure + "-24V" + suffix],
            result.GetProperty("symbols").EnumerateArray().Select(s => s.GetProperty("interruption").GetProperty("name").GetString()!).Order(StringComparer.Ordinal));
        JsonElement drawn = await eplan.CallAsync("eplan_object_find", new() { ["project"] = session.Project, ["page"] = page, ["object_type"] = "InterruptionPoint" });
        Assert.Equal(2, drawn.GetProperty("items").GetArrayLength());
        await session.UndoAsync();
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
    }

    /// <summary>A failure after the point exists: Abort left it on the page, so the same failure used to end in OUTCOME_UNKNOWN.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterThePointExists_RollsBack_AndWritesContinue(bool generate)
    {
        Assert.NotNull(host);
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string page = Assert.Single(await session.CreatePagesAsync(1));
        string suffix = page[(page.LastIndexOf('/') + 1)..];
        await PlaceSymbolsAsync(session, page, suffix);
        string before = (await session.SnapshotAsync(page)).Hash;
        (bool ok, JsonElement error) = await eplan.CallOrErrorAsync("eplan_connection_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["generate"] = generate,
            ["dry_run"] = false,
            ["items"] = new[] { new { kind = "interruption_point", name = "BAD" + suffix, position = _top, direction = "Down", property_arrangement = "No such arrangement" } },
        });
        TestContext.Current.TestOutputHelper?.WriteLine("result: " + error.GetRawText());
        Assert.False(ok);
        Assert.Equal("rolled_back", error.GetProperty("execution").GetString());
        Assert.Contains("Unknown property arrangement", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, (await session.SnapshotAsync(page)).Hash);
        (bool next, JsonElement placed) = await eplan.CallOrErrorAsync("eplan_connection_create", new()
        {
            ["project"] = session.Project,
            ["page"] = page,
            ["generate"] = false,
            ["dry_run"] = false,
            ["items"] = new[] { new { kind = "interruption_point", name = "NEXT" + suffix, position = _top, direction = "Down" } },
        });
        Assert.True(next, placed.GetRawText());
    }

    private async Task PlaceSymbolsAsync(PlanStepLiveSession session, string page, string suffix)
    {
        foreach ((string name, string tag, int y) in new[] { ("SOD", "-S1", 220), ("SSD", "-S2", 190), ("K", "-K2", 160) })
        {
            (bool placed, JsonElement device) = await eplan.CallOrErrorAsync("eplan_device_place", new()
            {
                ["project"] = session.Project,
                ["page"] = page,
                ["library"] = "IEC_symbol",
                ["symbol"] = name,
                ["symbol_variant"] = 0,
                ["x"] = 100,
                ["y"] = y,
                ["tag"] = tag + "X" + suffix,
                ["dry_run"] = false,
            });
            Assert.True(placed, device.GetRawText());
        }
    }
}
