using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_cable_get against the fixture's known data (api-notes A24, S21). What S21 did not verify (the cable
/// properties' texts, the time GetSourcesAndTargets takes, wire numbers on the cores) goes to the test output.
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class CableTests(LiveEplan eplan)
{
    private const string Cable = "+B1-WG2";

    [Fact]
    public async Task CableGet_ReadsTheSampleCableWithItsEndsAndTwoCores()
    {
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_cable_get", new() { ["cable"] = Cable, ["language"] = "en_US" });
        clock.Stop();

        JsonElement cable = data.GetProperty("cable");
        Assert.Equal(Cable, cable.GetProperty("tag").GetString());
        Assert.Equal(["=F+B1-SG1"], cable.GetProperty("sources").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(["+A2-XG4:2"], cable.GetProperty("targets").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.All(data.GetProperty("cores").EnumerateArray(), core => Assert.Equal("2d", core.GetProperty("kind").GetString()));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{clock.ElapsedMilliseconds} ms; type '{cable.GetProperty("type")}', wires '{cable.GetProperty("wireCount")}', "
            + $"cross-section '{cable.GetProperty("crossSection")}', length '{cable.GetProperty("length")}', parts {cable.GetProperty("parts")}; cores "
            + string.Join("; ", data.GetProperty("cores").EnumerateArray().Select(c => $"{c.GetProperty("colour")} / {c.GetProperty("designation")}: {c.GetProperty("from")} -> {c.GetProperty("to")}")));
    }

    [Fact]
    public async Task CableGet_ShownTag_FindsTheSameCable()
    {
        JsonElement data = await eplan.CallAsync("eplan_cable_get", new() { ["cable"] = "-WG2" });

        Assert.Equal(Cable, data.GetProperty("cable").GetProperty("tag").GetString());
    }

    [Fact]
    public async Task CableGet_EveryCableDeviceListFinds_IsACable()
    {
        JsonElement listed = await eplan.CallAsync("eplan_device_list", new() { ["category"] = "Cable", ["limit"] = 200 });
        var slowest = (Tag: string.Empty, Milliseconds: 0L);
        foreach (string tag in listed.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!))
        {
            var clock = Stopwatch.StartNew();
            await eplan.CallAsync("eplan_cable_get", new() { ["cable"] = tag });
            slowest = clock.ElapsedMilliseconds > slowest.Milliseconds ? (tag, clock.ElapsedMilliseconds) : slowest;
        }

        // S21 counted 78 functions of category Cable; the list keeps one per tag (A12), so it may show fewer.
        Assert.InRange(listed.GetProperty("total").GetInt32(), 1, 78);
        TestContext.Current.TestOutputHelper?.WriteLine($"Slowest of {listed.GetProperty("total")} cables: {slowest.Tag}, {slowest.Milliseconds} ms.");
    }

    [Fact]
    public async Task CableGet_DeviceThatIsNoCable_IsInvalidArgumentNamingItsCategory()
    {
        JsonElement error = await eplan.ErrorAsync("eplan_cable_get", new() { ["cable"] = "=GAA+A1-FC1" });

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal("CircuitBreaker", error.GetProperty("metadata").GetProperty("category").GetString());
    }
}
