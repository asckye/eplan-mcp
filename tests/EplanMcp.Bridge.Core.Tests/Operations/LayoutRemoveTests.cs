using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class LayoutRemoveTests
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };
    private readonly FakeLayout _layout = new();
    private readonly FakeTransactions _transactions = new();

    public LayoutRemoveTests()
    {
        _layout.Record.Objects.AddRange(
        [
            new LayoutObject { Space = "UC1", Id = 1, Kind = "Cabinet", Parts = ["RIT.1055500"] },
            new LayoutObject { Space = "UC1", Id = 2, Kind = "Component", ParentId = 1 },
            new LayoutObject { Space = "UC1", Id = 10, Kind = "MountingRail", ParentId = 2, Parts = ["PXC.0806602"] },
            new LayoutObject { Space = "UC1", Id = 11, Kind = "Component", Tag = "=K1+UC1-KE1", ParentId = 10, Parts = ["SIE.6ES7214"] },
            new LayoutObject { Space = "UC1", Id = 12, Kind = "Component", Tag = "=K1+UC1-KE2", ParentId = 10 },
            new LayoutObject { Space = "UC1", Id = 20, Kind = "Duct", ParentId = 2 },
        ]);
        foreach (LayoutObject o in _layout.Record.Objects.Where(o => o.Tag is not null))
        { _layout.Record.Names3D.Add(o.Tag!); }
    }

    [Fact]
    public void RailWithItsDevices_IsRemovedDevicesFirst_InOneTransaction()
    {
        var args = new LayoutRemoveArgs { Project = "Line1", Space = "UC1", Ids = [10, 11, 12] };
        WritePlan plan = Plan(args);
        Assert.Equal(["MountingRail", "Component", "Component"], plan.Changes.Select(c => c.Kind));
        Assert.Equal("=K1+UC1-KE1", plan.Changes[1].Name);

        args.Fingerprint = plan.Fingerprint;
        LayoutRemoveData data = Execute(args);

        Assert.Equal([11L, 12L, 10L], _layout.Removed);
        Assert.Equal([null, "=K1+UC1-KE1", "=K1+UC1-KE2"], data.Removed.Select(r => r.Tag));
        Assert.Equal(["Begin AI: eplan_layout_remove", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void RailAlone_EnclosureParts_UnknownAndDuplicateIds_AreRefused()
    {
        string violations = Assert.Throws<BridgeErrorException>(() => Plan(new LayoutRemoveArgs { Project = "Line1", Space = "UC1", Ids = [10, 1, 2, 99] })).Error.Metadata!["violations"];
        Assert.Contains("10: =K1+UC1-KE1 (11) is mounted on it", violations, StringComparison.Ordinal);
        Assert.Contains("1: a Cabinet is part of the enclosure", violations, StringComparison.Ordinal);
        Assert.Contains("2: a Component is part of the enclosure", violations, StringComparison.Ordinal);
        Assert.Contains("99: no placed object", violations, StringComparison.Ordinal);
        Assert.Contains("lists an ID twice", Assert.Throws<BridgeErrorException>(() => Plan(new LayoutRemoveArgs { Project = "Line1", Space = "UC1", Ids = [20, 20] })).Error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceStillNamedIn3D_RollsBack()
    {
        _layout.KeepNames = true;
        var args = new LayoutRemoveArgs { Project = "Line1", Space = "UC1", Ids = [11] };
        args.Fingerprint = Plan(args).Fingerprint;
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Contains("Rollback", _transactions.Calls);
    }

    [Fact]
    public void LayoutChangedAfterThePlan_IsStale()
    {
        var args = new LayoutRemoveArgs { Project = "Line1", Space = "UC1", Ids = [20] };
        args.Fingerprint = Plan(args).Fingerprint;
        _layout.Record.Objects.Add(new LayoutObject { Space = "UC1", Id = 21, Kind = "Duct", ParentId = 2 });
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_layout.Removed);
    }

    private WritePlan Plan(LayoutRemoveArgs args) => (WritePlan)new LayoutRemovePlanOperation(new FakeCatalog(_line1), _layout).Execute(args)!;

    private LayoutRemoveData Execute(LayoutRemoveArgs args) =>
        (LayoutRemoveData)new LayoutRemoveExecuteOperation(new FakeCatalog(_line1), _layout, _layout, _transactions).Execute(args)!;

    private sealed class FakeLayout : ILayoutReader, ILayoutRemover
    {
        public LayoutRecord Record { get; } = new();
        public List<long> Removed { get; } = [];
        public bool KeepNames { get; set; }

        public LayoutRecord Read(string projectPath) => Record;

        public void Remove(string projectPath, IReadOnlyList<long> ids)
        {
            foreach (long id in ids)
            {
                Removed.Add(id);
                LayoutObject gone = Record.Objects.Single(o => o.Id == id);
                Record.Objects.Remove(gone);
                if (!KeepNames && gone.Tag is not null)
                { Record.Names3D.Remove(gone.Tag); }
            }
        }
    }
}
