using System.Diagnostics;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_plc_get against the fixture's known data (api-notes A24, S21: 20 PLC boxes, =K1+A2-KE1 with 34 PLC terminals).
/// S21 read names only, so the address properties' texts, which symbolic address the user interface shows and where the
/// channels' connections are found go to the test output, for a check against the PLC navigator in EPLAN.
/// </summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlcTests(LiveEplan eplan)
{
    private const string Plc = "=K1+A2-KE1";

    [Fact]
    public async Task PlcGet_ReadsTheSamplePlcBoxWithItsChannels()
    {
        var clock = Stopwatch.StartNew();
        JsonElement data = await eplan.CallAsync("eplan_plc_get", new() { ["plc"] = Plc, ["language"] = "en_US" });
        clock.Stop();

        JsonElement plc = data.GetProperty("plc");
        JsonElement[] channels = [.. data.GetProperty("channels").EnumerateArray()];
        Assert.Equal(Plc, plc.GetProperty("tag").GetString());
        Assert.False(plc.GetProperty("cpu").GetBoolean());
        Assert.Equal(34, data.GetProperty("total").GetInt32());
        Assert.Equal(34, channels.Length);
        Assert.All(channels, c => Assert.StartsWith(Plc + ":", c.GetProperty("name").GetString(), StringComparison.Ordinal));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{clock.ElapsedMilliseconds} ms; station '{plc.GetProperty("station")}', rack '{plc.GetProperty("rack")}', slot '{plc.GetProperty("slot")}', parts {plc.GetProperty("parts")}; "
            + $"{channels.Count(c => c.GetProperty("connected").GetArrayLength() > 0)} channels connected.");
        foreach (JsonElement c in channels.Take(12))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{c.GetProperty("name")}: address '{c.GetProperty("address")}', symbolic '{c.GetProperty("symbolicAddress")}', type '{c.GetProperty("dataType")}', text '{c.GetProperty("text")}', connected {c.GetProperty("connected")}");
        }
    }

    [Fact]
    public async Task PlcGet_EveryPlcBoxDeviceListFinds_IsAPlcBox()
    {
        JsonElement listed = await eplan.CallAsync("eplan_device_list", new() { ["category"] = "PLCBox", ["limit"] = 200 });
        foreach (string tag in listed.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!))
        {
            await eplan.CallAsync("eplan_plc_get", new() { ["plc"] = tag, ["limit"] = 1 });
        }

        // S21 counted 20 PLC boxes; the list keeps one per tag (A12), so it may show fewer.
        Assert.InRange(listed.GetProperty("total").GetInt32(), 1, 20);
    }

    [Fact]
    public async Task PlcGet_DeviceThatIsNoPlcBox_IsInvalidArgumentNamingItsCategory()
    {
        JsonElement error = await eplan.ErrorAsync("eplan_plc_get", new() { ["plc"] = "=GAA+A1-FC1" });

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal("CircuitBreaker", error.GetProperty("metadata").GetProperty("category").GetString());
    }
}
