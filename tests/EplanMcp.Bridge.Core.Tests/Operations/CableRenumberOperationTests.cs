using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// cable.renumber.plan and cable.renumber.execute against a fake that numbers the way spike S21 found EPLAN numbers the
/// sample project's cables (api-notes A24: +A2-WD4 and +A2-WD5, +B2.Y1-WD1 and +B2.Y1-WD2 swap): the scheme check, how
/// given cables are found, the scope and the change list.
/// </summary>
[Trait("Size", "Small")]
public sealed class CableRenumberOperationTests
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly FakeDeviceReader _devices = new();
    private readonly FakeCableRenumberer _renumberer = new();

    public CableRenumberOperationTests()
    {
        // Two functions of +A2-WD4, as a category filter can give a device's tag more than once (A12).
        Cable("+A2-WD4", "-WD4", "c4");
        _renumberer.Cables.Add(new PlacedFunction { Name = "+A2-WD4", Id = "c4b" });
        Cable("+A2-WD5", "-WD5", "c5");
        Cable("+B2.Y1-WD1", "-WD1", "b1");
        Cable("+B2.Y1-WD2", "-WD2", "b2");
        Cable("+B3-WD1", "-WD1", "b3");
        Cable("+A1-?WE9", "-?WE9", "q");
        _devices.Functions.Add(new FunctionRecord { Name = "=GAA+A1-FC1", VisibleName = "-FC1", IsMain = true, Category = "CircuitBreaker" });
        _devices.Devices["=GAA+A1-FC1"] = new DeviceRecord { Main = new FunctionDetail { Name = "=GAA+A1-FC1", Category = "CircuitBreaker" } };
        _renumberer.Numbers["+A2-WD4"] = "+A2-WD5";
        _renumberer.Numbers["+A2-WD5"] = "+A2-WD4";
        _renumberer.Numbers["+B2.Y1-WD1"] = "+B2.Y1-WD2";
        _renumberer.Numbers["+B2.Y1-WD2"] = "+B2.Y1-WD1";
        _renumberer.Numbers["+A1-?WE9"] = "+A1-WE10";
    }

    [Fact]
    public void Plan_EveryCableOfTheProject()
    {
        WritePlan plan = Plan(new CableRenumberArgs { Project = "Line1", Scheme = " Standard " });

        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("update", "cables", "Line1", "numbering", null), (change.Action, change.Kind, change.Name, change.Property, change.NewValue));
        Assert.Equal("all cable tags in the project (6 cables), scheme: Standard, start 1, step 1", change.OldValue);
        Assert.Equal(("6", string.Empty, "false", "Standard", "1", "1"), (plan.Details!["cables"], plan.Details["cableCount"], plan.Details["keepExisting"], plan.Details["scheme"], plan.Details["start"], plan.Details["step"]));
        Assert.Empty(_renumberer.Renumbers);
    }

    [Fact]
    public void Plan_GivenCables_ByFullOrShownTag()
    {
        WritePlan plan = Plan(new CableRenumberArgs { Project = "Line1", Cables = ["+A2-WD4", " -wd5"], Scheme = "Standard", Start = 4, Step = 2 });

        Assert.Equal("all cable tags of the given cables (2 cables), scheme: Standard, start 4, step 2", Assert.Single(plan.Changes).OldValue);
        Assert.Equal(("2", "2"), (plan.Details!["cables"], plan.Details["cableCount"]));
    }

    [Fact]
    public void Plan_KeepExisting_CountsOnlyTagsWithQuestionMarks_AndWithoutOneHasNoChange()
    {
        WritePlan all = Plan(new CableRenumberArgs { Project = "Line1", Scheme = "Standard", KeepExisting = true });
        WritePlan none = Plan(new CableRenumberArgs { Project = "Line1", Cables = ["+A2-WD4"], Scheme = "Standard", KeepExisting = true });

        Assert.Equal("cable tags containing '?' in the project (1 cable), scheme: Standard, start 1, step 1", Assert.Single(all.Changes).OldValue);
        Assert.Empty(none.Changes);
        Assert.Equal("0", none.Details!["cables"]);
    }

    [Fact]
    public void Plan_OtherDevices_AreNamedWithTheirCategory_AndEveryMissingTagAtOnce()
    {
        BridgeError other = Assert.Throws<BridgeErrorException>(() => Plan(new CableRenumberArgs { Project = "Line1", Cables = ["-FC1", "+A9-WX1"], Scheme = "Standard" })).Error;
        BridgeError missing = Assert.Throws<BridgeErrorException>(() => Plan(new CableRenumberArgs { Project = "Line1", Cables = ["+A9-WX1", "+A2-WD4", "-WZ7"], Scheme = "Standard" })).Error;
        BridgeError ambiguous = Assert.Throws<BridgeErrorException>(() => Plan(new CableRenumberArgs { Project = "Line1", Cables = ["-WD1"], Scheme = "Standard" })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, "-FC1 (CircuitBreaker)"), (other.Reason, other.Metadata!["notCables"]));
        Assert.Equal((ErrorReason.DeviceNotFound, "+A9-WX1\n-WZ7"), (missing.Reason, missing.Metadata!["missing"]));
        Assert.Equal(ErrorReason.AmbiguousName, ambiguous.Reason);
        Assert.Contains("'cables'", ambiguous.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_ASchemeTheProjectDoesNotOffer_IsInvalidWithTheOfferedOnes()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(new CableRenumberArgs { Project = "Line1", Scheme = "standard" })).Error;

        Assert.Equal((ErrorReason.InvalidArgument, "Standard"), (error.Reason, error.Metadata!["candidates"]));
        Assert.StartsWith("EPLAN has no cable numbering scheme 'standard'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_NamesEveryArgumentProblemAtOnce()
    {
        BridgeError invalid = Assert.Throws<BridgeErrorException>(() => Plan(new CableRenumberArgs { Cables = ["-WD4", "-WD4", ""], Start = 0, Step = 0 })).Error;
        BridgeError empty = Assert.Throws<BridgeErrorException>(() => Plan(new CableRenumberArgs { Project = "Line1", Cables = [], Scheme = "Standard" })).Error;

        Assert.Equal(
            "'project' is required for writes: a name or path from eplan_project_list.\ncables[1] repeats cables[0].\ncables[2] is empty; give a cable tag such as +A2-WD4 (eplan_device_list with category Cable).\n'scheme' is required: a cable numbering scheme such as Standard.\n'start' must be at least 1.\n'step' must be at least 1.",
            invalid.Metadata!["violations"]);
        Assert.Equal("'cables' must hold 1 to 200 cable tags; omit it for every cable of the project.", empty.Metadata!["violations"]);
    }

    private void Cable(string tag, string shown, string id)
    {
        _renumberer.Cables.Add(new PlacedFunction { Name = tag, IsMain = true, Id = id });
        _devices.Functions.Add(new FunctionRecord { Name = tag, VisibleName = shown, IsMain = true, Category = "Cable" });
    }

    private WritePlan Plan(CableRenumberArgs args) => (WritePlan)new CableRenumberPlanOperation(new FakeCatalog(_project), _devices, _renumberer).Execute(args)!;
}
