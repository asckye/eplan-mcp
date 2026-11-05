using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

public sealed partial class DevicePartAssignmentTests
{
    [Fact]
    public void LiveBusbarInput_AddsTwoAccessoriesAfterBasePart()
    {
        const string busbar = "+A1-WE1", accessory = "RIT.9340030";
        _store.Targets[0].Name = _store.Targets[0].Tag = busbar;
        _store.Targets[0].Category = "BusBarDefText";
        _store.Targets[0].Parts.Add(new DevicePart { PartNumber = "RIT.3581000", Variant = "1", Count = 1 });
        _store.Database.Parts.Add(new PartSearchItem { PartNumber = accessory, Variant = "1" });
        var args = new DeviceUpdateArgs { Project = "Line1", Tag = busbar, Part = accessory, Variant = "1", Count = 2 };
        args.Fingerprint = Plan(args).Fingerprint;
        Execute(args);
        Assert.Equal(2, _store.Targets[0].Parts.Single(p => p.PartNumber == accessory).Count);
        Assert.Equal(2, Assert.Single(_store.Counts));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Count_DefaultOrExplicit_ControlsAddedAndVerifiedQuantity(int? requested, int expected)
    {
        const string accessory = "RIT.9340030";
        _store.Database.Parts.Add(new PartSearchItem { PartNumber = accessory, Variant = "1" });
        _store.Targets[0].Parts.Add(new DevicePart { PartNumber = "RIT.3581000", Variant = "1", Count = 1 });
        var args = new DeviceUpdateArgs { Project = "Line1", Tag = Tag, Part = accessory, Count = requested };
        WritePlan plan = Plan(args);
        Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), plan.Details!["count"]);
        args.Fingerprint = plan.Fingerprint;
        Execute(args);
        Assert.Equal(expected, Assert.Single(_store.Counts));
        Assert.Equal(expected, _store.Targets[0].Parts.Single(p => p.PartNumber == accessory).Count);
        Assert.Equal(1, _store.Targets[0].Parts.Single(p => p.PartNumber == "RIT.3581000").Count);
        Assert.True(_store.ReadAfterCommit);
    }

    [Theory]
    [InlineData(0, Part)]
    [InlineData(-1, Part)]
    [InlineData(2, null)]
    public void InvalidCountRefusesBeforeReading(int count, string? part)
    {
        var args = new DeviceUpdateArgs { Project = "Line1", Tag = Tag, NewTag = "-SF92", Part = part, Count = count };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(args)).Error;
        Assert.Contains("'count'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal(0, _store.Reads);
        Assert.Empty(_store.Adds);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void ChangingCountAfterPlanningRefusesWithoutWriting()
    {
        DeviceUpdateArgs args = Args();
        args.Fingerprint = Plan(args).Fingerprint;
        args.Count = 2;
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_store.Adds);
    }

    [Fact]
    public void WrongCommittedQuantityRollsBack()
    {
        DeviceUpdateArgs args = Args();
        args.Count = 2;
        args.Fingerprint = Plan(args).Fingerprint;
        _store.Mismatch = "count";
        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Execution);
        Assert.Empty(_store.Targets[0].Parts);
    }

    [Fact]
    public void ExplicitOneHasTheSamePlanAsDefault()
    {
        DeviceUpdateArgs args = Args();
        string defaultFingerprint = Plan(args).Fingerprint;
        args.Count = 1;
        Assert.Equal(defaultFingerprint, Plan(args).Fingerprint);
    }
}
