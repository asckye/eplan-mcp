using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>eplan_device_*, eplan_connection_list against the fixture's known data (api-notes A12).</summary>
[Trait("Size", "Large")]
public sealed class DeviceAndConnectionTests(LiveEplan eplan)
{
    private const string Breaker = "=GAA+A1-FC1";

    [Fact]
    public async Task DeviceList_CountsEveryDeviceOnce()
    {
        JsonElement all = await eplan.CallAsync("eplan_device_list", new() { ["limit"] = 1 });
        JsonElement onPage = await eplan.CallAsync("eplan_device_list", new() { ["page"] = "=GAA+A1&EFS1/1", ["limit"] = 200 });

        Assert.Equal(504, all.GetProperty("total").GetInt32());
        string[] tags = [.. onPage.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!)];
        Assert.Contains(Breaker, tags);
        Assert.Equal(tags.Length, tags.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DeviceGet_ShortTagInSeveralStructures_ListsTheCandidatesDeviceListFinds()
    {
        JsonElement error = await eplan.ErrorAsync("eplan_device_get", new() { ["tag"] = "-FC1" });
        JsonElement listed = await eplan.CallAsync("eplan_device_list", new() { ["tag_contains"] = "-FC1", ["limit"] = 200 });

        JsonElement metadata = error.GetProperty("metadata");
        string[] candidates = metadata.GetProperty("candidates").GetString()!.Split('\n');
        Assert.Equal(ErrorReason.AmbiguousName, error.GetProperty("reason").GetString());
        Assert.Equal("14", metadata.GetProperty("candidateCount").GetString());
        Assert.Contains(Breaker, candidates);
        Assert.Subset(listed.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()!).ToHashSet(StringComparer.Ordinal), candidates.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeviceGet_ReadsTheBreakerWithItsParts()
    {
        JsonElement data = await eplan.CallAsync("eplan_device_get", new() { ["tag"] = Breaker, ["language"] = "en_US" });

        JsonElement device = data.GetProperty("device");
        Assert.Equal(("-FC1", "CircuitBreaker", "=GAA+A1&EFS1/1"), (device.GetProperty("visibleTag").GetString(), device.GetProperty("category").GetString(), device.GetProperty("page").GetString()));
        Assert.Equal(["1", "2", "3", "4", "5", "6"], device.GetProperty("pins").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(
            [("SIE.3VA1032-2ED32-0AA0", "MCCB_IEC_FS100_32A_3P_16KA_TM_ FTFM"), ("SIE.3VA9157-0FK25", "DOOR MOUNTED ROTARY OPERATOR, EM")],
            data.GetProperty("parts").EnumerateArray().Select(p => (p.GetProperty("partNumber").GetString(), p.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task DeviceGet_UnknownTag_IsNotFound()
    {
        JsonElement error = await eplan.ErrorAsync("eplan_device_get", new() { ["tag"] = "=GAA+A1-FC999" });

        Assert.Equal(ErrorReason.DeviceNotFound, error.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ConnectionList_ConnectsTheBreakersLoadSideToItsMotorBreakers()
    {
        JsonElement data = await eplan.CallAsync("eplan_connection_list", new() { ["device"] = Breaker, ["limit"] = 200 });

        // The breaker may be either end of a connection; an end is null when it is not connected (eplan_connection_list spec).
        var otherEnd = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (JsonElement connection in data.GetProperty("connections").EnumerateArray())
        {
            (JsonElement from, JsonElement to) = (connection.GetProperty("from"), connection.GetProperty("to"));
            if (IsBreaker(from) && to.ValueKind == JsonValueKind.Object)
            {
                otherEnd[from.GetProperty("pin").GetString()!] = to.GetProperty("device").GetString();
            }
            else if (IsBreaker(to) && from.ValueKind == JsonValueKind.Object)
            {
                otherEnd[to.GetProperty("pin").GetString()!] = from.GetProperty("device").GetString();
            }
        }

        Assert.Equal(("=GAA+A1-FC2", "=GAA+A1-FC3", "=GAA+A1-FC4"), (otherEnd["2"], otherEnd["4"], otherEnd["6"]));
    }

    private static bool IsBreaker(JsonElement end) => end.ValueKind == JsonValueKind.Object && end.GetProperty("device").GetString() == Breaker;
}
