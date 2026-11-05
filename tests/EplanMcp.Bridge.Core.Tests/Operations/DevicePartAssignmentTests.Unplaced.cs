using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DevicePartAssignmentTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(4, null)]
    [InlineData(1, "Readback deviation: template remains unplaced.")]
    public void PartAssignmentReportsMergesAfterCommitWithinTheUndoStep(int count, string? warning)
    {
        _store.AssignFunctions = () => (count, warning);
        DeviceUpdateArgs args = Args();
        // A98's disclosed live request: add this exact part after the four named contacts are placed.
        args.Part = "SIE.3RT2015-1BB41-1AA0";
        _store.Database.Parts.Add(new PartSearchItem { PartNumber = args.Part, Variant = "1" });
        args.Fingerprint = Plan(args).Fingerprint;
        DeviceUpdateData result = Execute(args);
        Assert.Equal(count, result.AssignedFunctions);
        Assert.Equal(args.Part, Assert.Single(_store.Adds).Part);
        Assert.Equal(warning, result.Warning);
        Assert.Equal(["Begin AI: eplan_device_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }
}
