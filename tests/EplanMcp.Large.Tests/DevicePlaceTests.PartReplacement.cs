using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task PlacedDevice_PartIsReplaced_ReferencesReadBack_UndoRestores()
    {
        await using var session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project;
        string page = await NewPageAsync(project);
        JsonElement source = Assert.Single((await PartsCallAsync("eplan_part_search", new() { ["query"] = Part }))
            .GetProperty("parts").EnumerateArray(), p => p.GetProperty("partNumber").GetString() == Part && p.GetProperty("variant").GetString() == "1");
        string group = source.GetProperty("productGroup").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(group));
        var search = new Dictionary<string, object?> { ["query"] = "SIE.", ["manufacturer"] = source.GetProperty("manufacturer").GetString(), ["limit"] = 100 };
        JsonElement candidate;
        do
        {
            JsonElement matches = await PartsCallAsync("eplan_part_search", search);
            candidate = matches.GetProperty("parts").EnumerateArray().FirstOrDefault(p => p.GetProperty("partNumber").GetString() != Part
                && p.GetProperty("productGroup").GetString() == group && !string.IsNullOrEmpty(p.GetProperty("variant").GetString()));
            search["cursor"] = matches.GetProperty("nextCursor").GetString();
        }
        while (candidate.ValueKind == JsonValueKind.Undefined && search["cursor"] is not null);
        Assert.True(candidate.ValueKind == JsonValueKind.Object, $"No second {group} part found.");
        string number = candidate.GetProperty("partNumber").GetString()!;
        string variant = candidate.GetProperty("variant").GetString()!;
        JsonElement placed = await PartsCallAsync("eplan_device_place", new()
        { ["project"] = project, ["page"] = page, ["part"] = Part, ["x"] = 80, ["y"] = 160, ["tag"] = "-KF9" + page[Prefix.Length..], ["dry_run"] = false });
        string tag = placed.GetProperty("device").GetProperty("tag").GetString()!;
        JsonElement before = (await AssignmentDeviceAsync(project, tag)).GetProperty("parts");
        int count = Assert.Single(before.EnumerateArray(), p => p.GetProperty("partNumber").GetString() == Part).GetProperty("count").GetInt32();
        JsonElement result = await PartsCallAsync("eplan_device_update", new()
        { ["project"] = project, ["tag"] = tag, ["part"] = number, ["variant"] = variant, ["replace_part"] = Part, ["dry_run"] = false });
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Equal(Part, result.GetProperty("replacedPart").GetProperty("partNumber").GetString());
        JsonElement after = (await AssignmentDeviceAsync(project, tag)).GetProperty("parts");
        Assert.Equal(before.EnumerateArray().Select(p => p.GetProperty("partNumber").GetString() == Part ? (number, variant, count) : Signature(p)),
            after.EnumerateArray().Select(Signature));
        await session.UndoAsync();
        Assert.True(JsonElement.DeepEquals(before, (await AssignmentDeviceAsync(project, tag)).GetProperty("parts")));

        static (string?, string?, int) Signature(JsonElement p) =>
            (p.GetProperty("partNumber").GetString(), p.GetProperty("variant").GetString(), p.GetProperty("count").GetInt32());
    }
}
