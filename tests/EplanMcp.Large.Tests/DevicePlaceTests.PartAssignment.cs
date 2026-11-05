using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

public sealed partial class DevicePlaceTests
{
    [Fact]
    public async Task PartAssignment_MergesMatchingPlacedContacts_OneUndoRestores()
    {
        await using PlanStepLiveSession session = await PlanStepLiveSession.StartAsync(eplan);
        string project = session.Project, page = await NewPageAsync(project);
        string tag = "=GAA+A1-KPA" + page[Prefix.Length..];
        try
        {
            Dictionary<string, object?> coil = AssignmentSymbol(project, page, tag, "K", 60);
            coil.Remove("join_device");
            JsonElement placed = await PartsCallAsync("eplan_device_place", coil);
            tag = placed.GetProperty("device").GetProperty("tag").GetString()!;
            for (int index = 0; index < _assignmentPinPairs.Length; index++)
            {
                Dictionary<string, object?> contact = AssignmentSymbol(project, page, tag, "S", 100 + index * 40);
                contact["connection_designations"] = _assignmentPinPairs[index];
                await PartsCallAsync("eplan_device_place", contact);
            }

            JsonElement original = await AssignmentDeviceAsync(project, tag);
            AssertAssignmentContacts(original, placed: 4);
            Assert.Empty(original.GetProperty("parts").EnumerateArray());
            string before = (await session.SnapshotAsync()).Hash;
            JsonElement result = await PartsCallAsync("eplan_device_update", new()
            {
                ["project"] = project,
                ["tag"] = tag,
                ["part"] = AssignmentPart,
                ["variant"] = "1",
                ["dry_run"] = false
            });
            Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(4, result.GetProperty("assignedFunctions").GetInt32());
            JsonElement read = await AssignmentDeviceAsync(project, tag);
            AssertAssignmentContacts(read, placed: 4);
            Assert.Equal(original.GetProperty("functions").EnumerateArray().Select(function => function.GetRawText()).Order(StringComparer.Ordinal),
                read.GetProperty("functions").EnumerateArray().Select(function => function.GetRawText()).Order(StringComparer.Ordinal));
            Assert.Equal(AssignmentPart, Assert.Single(read.GetProperty("parts").EnumerateArray()).GetProperty("partNumber").GetString());
            await session.UndoAsync();
            Assert.Equal(before, (await session.SnapshotAsync()).Hash);
            Assert.Equal(original.GetRawText(), (await AssignmentDeviceAsync(project, tag)).GetRawText());
        }
        catch
        {
            await PartsFailureReadbackAsync(project, tag);
            await AssignmentPageFailureAsync(project, page);
            throw;
        }
    }
}
