using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    private const string AssignmentPart = "SIE.3RT2015-1BB41-1AA0";
    private static readonly string[][] _assignmentPinPairs = [["1/L1", "2/T1"], ["3/L2", "4/T2"], ["5/L3", "6/T3"], ["13", "14"]];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JoinDevice_AssignsFourUnplacedContacts_OneUndoRestores(bool explicitPinNames)
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project, page = await NewPageAsync(project);
        string tag = "=GAA+A1-KUA" + page[Prefix.Length..];
        try
        {
            Dictionary<string, object?> coil = AssignmentSymbol(project, page, tag, "K", 60);
            coil.Remove("join_device");
            coil["part"] = AssignmentPart;
            JsonElement placed = await PartsCallAsync("eplan_device_place", coil);
            tag = placed.GetProperty("device").GetProperty("tag").GetString()!;
            JsonElement original = await AssignmentDeviceAsync(project, tag);
            AssertAssignmentContacts(original, placed: 0);
            var remainingPins = _assignmentPinPairs.Select(pins => string.Join("|", pins)).ToHashSet(StringComparer.Ordinal);
            for (int index = 0; index < 4; index++)
            {
                Dictionary<string, object?> request = AssignmentSymbol(project, page, tag, "S", 100 + index * 40);
                if (explicitPinNames)
                { request["connection_designations"] = _assignmentPinPairs[index]; }
                string before = (await session.SnapshotAsync()).Hash;
                JsonElement result = await PartsCallAsync("eplan_device_place", request);
                Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
                Assert.True(result.GetProperty("assignedFunction").GetBoolean());
                Assert.Equal(5, result.GetProperty("functions").GetInt32());
                string[] pins = [.. result.GetProperty("pins").EnumerateArray().Select(pin => pin.GetString()!)];
                Assert.True(remainingPins.Remove(string.Join("|", pins)), result.GetRawText());
                if (explicitPinNames)
                { Assert.Equal(_assignmentPinPairs[index], pins); }
                Assert.Equal(3 - index, result.GetProperty("unplaced").GetInt32());
                AssertAssignmentContacts(await AssignmentDeviceAsync(project, tag), placed: index + 1);
                if (index == 3)
                {
                    await session.UndoAsync();
                    Assert.Equal(before, (await session.SnapshotAsync()).Hash);
                    AssertAssignmentContacts(await AssignmentDeviceAsync(project, tag), placed: 3);
                    await PartsCallAsync("eplan_device_place", request);
                }
            }

            Assert.Empty(remainingPins);
            JsonElement complete = await AssignmentDeviceAsync(project, tag);
            AssertAssignmentContacts(complete, placed: 4);
            string beforeFifth = (await session.SnapshotAsync()).Hash;
            JsonElement fifth = await PartsCallAsync("eplan_device_place", AssignmentSymbol(project, page, tag, "S", 280));
            Assert.Equal(ExecutionState.Committed, fifth.GetProperty("execution").GetString());
            Assert.False(fifth.TryGetProperty("assignedFunction", out JsonElement assigned) && assigned.GetBoolean());
            JsonElement extra = await AssignmentDeviceAsync(project, tag);
            Assert.Equal(5, extra.GetProperty("functions").GetArrayLength());
            Assert.All(extra.GetProperty("functions").EnumerateArray(), function => Assert.Equal(page, function.GetProperty("page").GetString()));
            await session.UndoAsync();
            Assert.Equal(beforeFifth, (await session.SnapshotAsync()).Hash);
            AssertAssignmentContacts(await AssignmentDeviceAsync(project, tag), placed: 4);
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            await AssignmentPageFailureAsync(project, page);
            throw;
        }
    }

    private static Dictionary<string, object?> AssignmentSymbol(string project, string page, string tag, string symbol, int x) => new()
    {
        ["project"] = project,
        ["page"] = page,
        ["tag"] = tag,
        ["library"] = "IEC_symbol",
        ["symbol"] = symbol,
        ["symbol_variant"] = 0,
        ["join_device"] = true,
        ["x"] = x,
        ["y"] = 160,
        ["dry_run"] = false
    };

    private Task<JsonElement> AssignmentDeviceAsync(string project, string tag) =>
        PartsCallAsync("eplan_device_get", new() { ["project"] = project, ["tag"] = tag });

    private async Task AssignmentPageFailureAsync(string project, string page)
    {
        try
        { await PartsCallAsync("eplan_page_get", new() { ["project"] = project, ["page"] = page, ["include_drawings"] = true }); }
        catch (Exception failure) { TestContext.Current.TestOutputHelper!.WriteLine("Failure page readback: " + failure); }
    }

    private static void AssertAssignmentContacts(JsonElement read, int placed)
    {
        JsonElement[] functions = [.. read.GetProperty("functions").EnumerateArray()];
        Assert.Equal("Coil", read.GetProperty("device").GetProperty("category").GetString());
        Assert.Equal(4, functions.Length);
        Assert.All(functions, function => Assert.Equal("NoContact", function.GetProperty("category").GetString()));
        Assert.All(functions, function => Assert.Equal(read.GetProperty("device").GetProperty("tag").GetString(), function.GetProperty("tag").GetString()));
        Assert.Equal(placed, functions.Count(function => function.GetProperty("page").ValueKind != JsonValueKind.Null));
        Assert.Equal(_assignmentPinPairs.Select(pins => string.Join("|", pins)).Order(StringComparer.Ordinal),
            functions.Select(function => string.Join("|", function.GetProperty("pins").EnumerateArray().Select(pin => pin.GetString()))).Order(StringComparer.Ordinal));
    }
}
