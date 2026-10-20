using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// device.update.plan and device.update.execute against fake functions (docs/tools/eplan_device_update.md, sections 2,
/// 4, 5 and 7). The device =F+A2-KF1 has a coil and two contacts, one of them on another page (S9).
/// </summary>
[Trait("Size", "Small")]
public sealed class DeviceUpdateOperationTests
{
    private readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true };
    private readonly FakeCatalog _catalog;
    private readonly FakeDeviceReader _reader = new();
    private readonly FakeDeviceRenamer _renamer = new();
    private readonly FakeTransactions _transactions = new();

    public DeviceUpdateOperationTests()
    {
        _catalog = new FakeCatalog(_line1);
        _renamer.Functions.AddRange(
        [
            ("=F+A2-KF1", "1/1/1/0", true, "=F+A2&EFS1/1", "KF1"),
            ("=F+A2-KF1", "1/1/2/0", false, "=F+A2&EFS1/1", string.Empty),
            ("=F+A2-KF1", "1/1/3/0", false, "=K1+A2&EFS1/2", string.Empty),
            ("=F+A2-KF2", "1/1/4/0", true, "=F+A2&EFS1/1", "KF2"),
            ("+A1-XG5:1", "1/1/5/0", true, "=F+A2&EFS1/3", "XG5:1"),
            ("=G+A9-KF1", "1/1/6/0", true, "=G+A9&EFS1/1", "KF1"),
        ]);
        _reader.Functions.AddRange(_renamer.Functions.Where(f => f.IsMain).Select(f => new FunctionRecord { Name = f.Name, VisibleName = "-" + f.DeviceTag, IsMain = true }));
        _transactions.Snapshot = () =>
        {
            var before = _renamer.Functions.ToList();
            return () =>
            {
                _renamer.Functions.Clear();
                _renamer.Functions.AddRange(before);
            };
        };
    }

    [Fact]
    public void Plan_IsOneTagChangeWithTheFunctionsAndPages()
    {
        WritePlan plan = Plan(Args("=F+A2-KF1", "-KF19"));

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "device", "=F+A2-KF1", "tag", "=F+A2-KF1", "=F+A2-KF19"), (change.Action, change.Kind, change.Name, change.Property, change.OldValue, change.NewValue));
        Assert.Equal(("3", "2", "=F+A2&EFS1/1\n=K1+A2&EFS1/2"), (plan.Details!["functions"], plan.Details["pageCount"], plan.Details["pages"]));
        Assert.Empty(_renamer.Renames);
    }

    [Theory]
    [InlineData("=F+A2-KF1", "=X+A2-KF1")]
    [InlineData("=F+A2-KF1", "=F+A2-kf1")]
    [InlineData("=F+A2-KF2", "-KF20")]
    public void Plan_TakesFullOrShortNewTagsAndCaseChanges(string tag, string newTag)
    {
        Assert.Single(Plan(Args(tag, newTag)).Changes);
    }

    [Theory]
    [InlineData("", "is required")]
    [InlineData("=F+A2 -KF9", "white space")]
    [InlineData("=F+A2-KF9:1", "':'")]
    [InlineData("KF9", "device part")]
    [InlineData("=F+A2-", "device part")]
    [InlineData("=F+A2-KF1", "already")]
    public void Plan_RejectsANewTagWithTheReason(string newTag, string reason)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=F+A2-KF1", newTag))).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains(reason, error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_FindsTheDeviceByItsShownTagOrSaysWhichOnesMatch()
    {
        Assert.Equal("=F+A2-KF2", Plan(Args("-KF2", "-KF22")).Changes[0].Name);
        BridgeError ambiguous = Assert.Throws<BridgeErrorException>(() => Plan(Args("-KF1", "-KF19"))).Error;
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(Args("-KF7", "-KF19"))).Error;

        Assert.Equal(ErrorReason.AmbiguousName, ambiguous.Reason);
        Assert.Equal(["=F+A2-KF1", "=G+A9-KF1"], ambiguous.Metadata!["candidates"].Split('\n'));
        Assert.Equal(ErrorReason.DeviceNotFound, missing.Reason);
    }

    [Theory]
    [InlineData("=F+A2-KF2")]
    [InlineData("=f+a2-kf2")]
    [InlineData("+A1-XG5")]
    public void Plan_ATagInUse_IsAlreadyExists(string newTag)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=F+A2-KF1", newTag))).Error;

        Assert.Equal(ErrorReason.AlreadyExists, error.Reason);
        Assert.Contains("merge", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ADeviceWithSuffixedFunctions_IsRefused()
    {
        _renamer.SubFunctions["1/1/1/0"] = ["=F+A2-KF1", "=F+A2-KF1:13"];

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=F+A2-KF1", "-KF19"))).Error;

        Assert.Contains("'=F+A2-KF1:13'", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_AShortNewTagWhenTheStructureIsUnclear_AsksForTheFullTag()
    {
        _renamer.Functions[0] = _renamer.Functions[0] with { DeviceTag = string.Empty };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(Args("=F+A2-KF1", "-KF19"))).Error;

        Assert.Contains("in full", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RenamesTheMainAndEveryAuxiliaryFunction()
    {
        DeviceUpdateArgs args = Args("=F+A2-KF1", "-KF19");
        args.Fingerprint = Plan(args).Fingerprint;

        DeviceUpdateData data = Execute(args);

        Assert.Equal(("Line1", "=F+A2-KF19", "=F+A2-KF1", "-KF19", 3), (data.Project, data.Tag, data.PreviousTag, data.VisibleTag, data.Functions));
        Assert.Equal(["=F+A2&EFS1/1", "=K1+A2&EFS1/2"], data.Pages);
        Assert.Equal(("=F+A2-KF1", "=F+A2-KF19"), Assert.Single(_renamer.Renames));
        Assert.Equal(["Begin AI: eplan_device_update", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void Execute_AfterTheDeviceChanged_IsPlanStale()
    {
        DeviceUpdateArgs args = Args("=F+A2-KF1", "-KF19");
        args.Fingerprint = Plan(args).Fingerprint;
        _renamer.Functions.Add(("=F+A2-KF1", "1/1/9/0", false, "=F+A2&EFS1/4", string.Empty)); // a contact the engineer added

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_renamer.Renames);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void Execute_AfterTheNewTagCameIntoUse_IsPlanStale()
    {
        DeviceUpdateArgs args = Args("=F+A2-KF1", "-KF19");
        args.Fingerprint = Plan(args).Fingerprint;
        _renamer.Functions.Add(("=F+A2-KF19", "1/1/9/0", true, "=F+A2&EFS1/4", "KF19"));

        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
    }

    [Fact]
    public void Execute_ATagPartEplanDrops_RollsTheRenameBack()
    {
        // S9: a cable given =F+A2-KF19 became +A2-KF19.
        DeviceUpdateArgs args = Args("=F+A2-KF1", "=X+A2-KF19");
        args.Fingerprint = Plan(args).Fingerprint;
        _renamer.Drop = "=X";

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack, "=X+A2-KF19", "+A2-KF19"), (error.Reason, error.Execution, error.Metadata!["requested"], error.Metadata["actual"]));
        Assert.Equal(3, _renamer.Functions.Count(f => f.Name == "=F+A2-KF1"));
    }

    [Fact]
    public void Execute_AFunctionLeftWithTheOldTag_RollsTheRenameBack()
    {
        DeviceUpdateArgs args = Args("=F+A2-KF1", "-KF19");
        args.Fingerprint = Plan(args).Fingerprint;
        _renamer.LeaveBehind = true;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(args)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(3, _renamer.Functions.Count(f => f.Name == "=F+A2-KF1"));
    }

    private static DeviceUpdateArgs Args(string tag, string newTag) => new() { Project = "Line1", Tag = tag, NewTag = newTag };

    private WritePlan Plan(DeviceUpdateArgs args) =>
        (WritePlan)new DeviceUpdatePlanOperation(_catalog, _reader, _renamer).Execute(args)!;

    private DeviceUpdateData Execute(DeviceUpdateArgs args) =>
        (DeviceUpdateData)new DeviceUpdateExecuteOperation(_catalog, _reader, _renamer, _transactions).Execute(args)!;
}
