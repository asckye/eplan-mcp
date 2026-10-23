using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Only read operations on the fixture and EPLAN's sample macro directory; no insertion or fixture writes.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PlanMacroInspectTests(LiveEplan eplan)
{
    [Fact]
    public async Task PlanMacroInspect_SampleVariantZero_MatchesMacroGetContentAndFullCount()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        Dictionary<string, string> before = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        (string path, JsonElement get, int checkedCount) = await FindSupportedMacroAsync();
        TestContext.Current.TestOutputHelper!.WriteLine($"Selected macro {path} after checking {checkedCount} macros: macro.get reports {get.GetProperty("deviceCount").GetInt32()} distinct devices, {get.GetProperty("objectCount").GetInt32()} placements.");
        PlanMacroInspectData data = await InspectAsync(bridge, path);

        Assert.Matches("^[0-9a-f]{64}$", data.Sha256);
        Assert.Equal(data.Sha256, (await InspectAsync(bridge, path)).Sha256);
        List<PlanMacroFunction> devices = [.. data.Functions.Where(f => f.Category != "Wire" && f.Name.Length > 0)
            .GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.FirstOrDefault(f => f.VisibleName.Length > 0) ?? g.First())];
        Assert.Equal(get.GetProperty("deviceCount").GetInt32(), devices.Count);
        JsonElement[] first = [.. get.GetProperty("devices").EnumerateArray()];
        Assert.Equal(Math.Min(50, devices.Count), first.Length);
        Assert.Equal(first.Select(f => (f.GetProperty("tag").GetString(), f.GetProperty("visibleTag").GetString(), f.GetProperty("category").GetString(), f.GetProperty("main").GetBoolean())),
            devices.Take(50).Select(f => ((string?)f.Name, f.VisibleName.Length == 0 ? null : f.VisibleName, (string?)f.Category, f.Main)));
        Assert.Equal(get.GetProperty("objectCount").GetInt32(), data.ObjectCount);
        Assert.InRange(data.Functions.Count, devices.Count, data.ObjectCount);
        Assert.Equal(get.GetProperty("size").GetProperty("width").GetDouble(), Math.Round(data.Frame.Right - data.Frame.Left, 2));
        Assert.Equal(get.GetProperty("size").GetProperty("height").GetDouble(), Math.Round(data.Frame.Top - data.Frame.Bottom, 2));
        Assert.All(data.Functions, f =>
        {
            Assert.True(f.Kind is "function" or "terminal" or "plc" or "cable");
            Assert.DoesNotContain("PLCTerminal", f.Category, StringComparison.Ordinal);
            Assert.All(f.StructureSegments, s => Assert.True(s[0] is '=' or '+'));
        });
        string json = JsonSerializer.Serialize(data);
        Assert.DoesNotContain("SourceProject", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(eplan.ProjectPath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), LiveSnapshots.DesignFiles(eplan.ProjectPath).OrderBy(p => p.Key, StringComparer.Ordinal));
        TestContext.Current.TestOutputHelper!.WriteLine($"Variant 0 of {path}: {data.Functions.Count} functions, {devices.Count} distinct devices, {data.ObjectCount} placements; SHA-256 {data.Sha256}.");
    }

    [Fact]
    public async Task PlanMacroInspect_FeedbackControlWithPlcTerminals_IsRefused()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        JsonElement get = (await eplan.CallAsync("eplan_macro_get", new() { ["macro"] = MacroTests.FeedbackControl, ["project"] = eplan.ProjectPath })).GetProperty("macro");
        int plcTerminals = get.GetProperty("devices").EnumerateArray().Count(d => d.GetProperty("category").GetString()!.StartsWith("PLCTerminal", StringComparison.Ordinal));
        Assert.True(plcTerminals > 0, "The refused sample must contain PLC terminals with external structure (A14).");
        TestContext.Current.TestOutputHelper!.WriteLine($"Refused sample macro {MacroTests.FeedbackControl}: {get.GetProperty("deviceCount").GetInt32()} distinct devices, {get.GetProperty("objectCount").GetInt32()} placements, {plcTerminals} PLC terminals.");

        BridgeCallException refused = await Assert.ThrowsAsync<BridgeCallException>(() => InspectAsync(bridge, MacroTests.FeedbackControl));
        Assert.Equal(ErrorReason.CapabilityMissing, refused.Error.Reason);
        Assert.Null(refused.Error.Execution);
        using JsonDocument missing = JsonDocument.Parse(refused.Error.Metadata!["missing"]);
        Assert.Equal("macro:structure_ownership", Assert.Single(missing.RootElement.EnumerateArray()).GetString());
    }

    private async Task<(string Path, JsonElement Macro, int CheckedCount)> FindSupportedMacroAsync()
    {
        int checkedCount = 0;
        string? cursor = null;
        do
        {
            JsonElement list = await eplan.CallAsync("eplan_macro_list", new() { ["limit"] = 200, ["cursor"] = cursor });
            string? next = list.GetProperty("nextCursor").GetString();
            Assert.True(!list.GetProperty("truncated").GetBoolean() || next is not null,
                "A truncated final macro list cannot establish whether the sample has a supported macro.");
            foreach (JsonElement item in list.GetProperty("macros").EnumerateArray())
            {
                string path = item.GetProperty("path").GetString()!;
                JsonElement get = (await eplan.CallAsync("eplan_macro_get", new() { ["macro"] = path, ["project"] = eplan.ProjectPath })).GetProperty("macro");
                checkedCount++;
                bool multiLineZero = get.GetProperty("representations").EnumerateArray().Any(r => r.GetProperty("type").GetString() == "MultiLine"
                    && r.GetProperty("variants").EnumerateArray().Any(v => v.GetInt32() == 0));
                bool supportedTags = get.GetProperty("devices").EnumerateArray().All(d =>
                    !d.GetProperty("category").GetString()!.StartsWith("PLCTerminal", StringComparison.Ordinal)
                    && (d.GetProperty("category").GetString() == "Wire" || d.GetProperty("tag").GetString()!.Contains('-', StringComparison.Ordinal)));
                if (path.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) && multiLineZero && supportedTags)
                {
                    return (path, get, checkedCount);
                }
            }

            if (next is not null)
            {
                Assert.NotEqual(cursor, next);
            }
            cursor = next;
        }
        while (cursor is not null);

        Assert.Skip($"No supported macro found after checking all {checkedCount} macros through eplan_macro_list pages: requires MultiLine variant 0, no PLC terminals, and '-' in every non-wire tag.");
        return default;
    }

    private static Task<PlanMacroInspectData> InspectAsync(BridgeClient bridge, string macro) =>
        bridge.InvokeAsync<PlanMacroInspectArgs, PlanMacroInspectData>(OperationNames.PlanMacroInspect, new() { Macro = macro }, TestContext.Current.CancellationToken);
}
