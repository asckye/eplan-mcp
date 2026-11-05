using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // A39: fake native functions belong to their project and must not be disposed.

[Trait("Size", "Small")]
public sealed class SymbolFunctionAssignmentTests
{
    [Theory]
    [InlineData(null, true, true)]
    [InlineData("1/L1,2/T1", true, true)]
    [InlineData("2/T1,1/L1", true, false)]
    [InlineData("13,14", true, false)]
    [InlineData(null, false, false)]
    public void Join_AssignsFirstMatchingTemplate_AfterCommitInOneUndo(string? requested, bool sameCategory, bool assigned)
    {
        FakeMoveNative.Calls.Clear();
        FakeMoveNative.EndTransaction = null;
        var page = new Page { Name = "=GAA+A1&EFS1/1" };
        var variant = new SymbolVariant { SymbolLibraryName = "IEC_symbol", SymbolName = "S", VariantNr = 0 };
        var project = new Project(page)
        { SymbolLibraries = [new() { Name = "IEC_symbol", Symbols = [new() { Name = "S", Variants = [variant] }] }] };
        EplanProjects.Project = project;
        Function Template(long id) => new(id)
        {
            Name = "=GAA+A1-K1",
            IsPlaced = false,
            FunctionCategory = sameCategory ? FunctionCategory.NoContact : FunctionCategory.Contactor,
            Pins = [new() { Name = "1/L1" }, new() { Name = "2/T1" }],
            SymbolVariantReadError = new InvalidOperationException("Unplaced SymbolVariant is unreadable")
        };
        Function first = Template(2), later = Template(9);
        project.UnplacedFunctions = [later, first];
        var args = new DevicePlaceArgs
        {
            Library = "IEC_symbol",
            Symbol = "S",
            SymbolVariant = 0,
            Tag = "=GAA+A1-K1",
            JoinDevice = true,
            X = 80,
            Y = 160
        };
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name,
            new() { Library = "IEC_symbol", Name = "S", Variant = 0, Kind = "function", Type = "Function" }, true);
        first.OnAssign = target =>
        {
            Assert.True(FakeMoveNative.Calls.IndexOf("Commit") < FakeMoveNative.Calls.LastIndexOf("Transaction"));
            Assert.DoesNotContain("CloseUndo", FakeMoveNative.Calls);
            target.Pins = first.Pins;
            project.UnplacedFunctions = [later];
        };
        later.OnAssign = _ => Assert.Fail("The lowest ObjectIdentifier must win.");
        var result = TransactionRunner.Run(adapter, "eplan_device_place", () =>
        {
            DevicePlaceData data = adapter.Place(args, args.Tag);
            Assert.Single(page.AllPlacements.OfType<Function>()).FunctionCategory = FunctionCategory.NoContact;
            args.ConnectionDesignations = requested?.Split(',');
            return data;
        }, _ => true, data => adapter.AssignUnplacedFunction(args, data));
        Assert.Equal(assigned ? true : null, result.Result.AssignedFunction);
        Assert.Single(FakeMoveNative.Calls, call => call == "CloseUndo");
        if (assigned)
        {
            Assert.Equal(["1/L1", "2/T1"], result.Result.Pins!);
            Assert.Equal(1, result.Result.Unplaced);
            Assert.Equal(2, result.Result.Functions);
        }
        else
        { Assert.Null(result.Result.Pins); Assert.Equal(2, project.UnplacedFunctions.Length); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AssignedReadbackDeviation_WarnsWithoutThrowing(bool wrongPins, bool sourceRemains)
    {
        FakeMoveNative.EndTransaction = null;
        var page = new Page();
        var target = new Function(1) { Name = "-K1", Page = page };
        var source = new Function(2) { Name = "-K1", Pins = [new() { Name = "13" }, new() { Name = "14" }] };
        var project = new Project(page) { UnplacedFunctions = [source] };
        EplanProjects.Project = project;
        page.AllPlacements = [target];
        source.OnAssign = actual =>
        {
            actual.Pins = wrongPins ? [] : source.Pins;
            if (!sourceRemains)
            { project.UnplacedFunctions = []; }
        };
        EplanFunctionAssignment.Result result = EplanFunctionAssignment.Assign("project", 2, 1, "-K1");
        Assert.Contains("Readback deviation", result.Warning, StringComparison.Ordinal);
        Assert.Equal(sourceRemains ? 2 : 1, result.Functions);
    }

    [Fact]
    public void UnreadableFunctionsInAssignmentTransaction_WarnWithoutClaimingAssignment()
    {
        FakeMoveNative.EndTransaction = null;
        FakeMoveNative.Calls.Clear();
        EplanProjects.Project = new Project { FindFunctions = _ => throw new InvalidOperationException("Unreadable functions") };
        EplanFunctionAssignment.Result result = EplanFunctionAssignment.Assign("project", 2, 1, "-K1");
        Assert.False(result.Assigned);
        Assert.Contains("could not be read for assignment", result.Warning, StringComparison.Ordinal);
        Assert.DoesNotContain("Commit", FakeMoveNative.Calls);
    }

    [Fact]
    public void UnreadableTemplatePins_StillAssignsAndWarns()
    {
        FakeMoveNative.EndTransaction = null;
        var page = new Page();
        var target = new Function(1) { Name = "-K1", Page = page };
        var source = new Function(2) { Name = "-K1", PinsReadError = new InvalidOperationException("Unreadable pins") };
        var project = new Project(page) { UnplacedFunctions = [source] };
        EplanProjects.Project = project;
        page.AllPlacements = [target];
        source.OnAssign = actual => { actual.Pins = [new() { Name = "13" }]; project.UnplacedFunctions = []; };
        EplanFunctionAssignment.Result result = EplanFunctionAssignment.Assign("project", 2, 1, "-K1");
        Assert.Contains("verification is incomplete", result.Warning, StringComparison.Ordinal);
        Assert.Equal(["13"], result.Pins!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignmentReadback_UnavailableOrNoTemplates_DoesNotRefuse(bool unreadable)
    {
        FakeMoveNative.EndTransaction = null;
        var page = new Page();
        var variant = new SymbolVariant { SymbolLibraryName = "IEC_symbol", SymbolName = "S", VariantNr = 0 };
        var project = new Project(page) { SymbolLibraries = [new() { Name = "IEC_symbol", Symbols = [new() { Name = "S", Variants = [variant] }] }] };
        EplanProjects.Project = project;
        var adapter = new EplanDeviceSymbolPlacement("project", page.Name, new() { Library = "IEC_symbol", Name = "S", Type = "Function" }, true);
        var args = new DevicePlaceArgs { Tag = "-K1", JoinDevice = true };
        DevicePlaceData data = adapter.Place(args, args.Tag);
        if (unreadable)
        { project.FindFunctions = _ => throw new InvalidOperationException("Unreadable functions"); }
        adapter.AssignUnplacedFunction(args, data);
        Assert.Null(data.AssignedFunction);
        if (unreadable)
        { Assert.Contains("could not be read", data.Warning, StringComparison.Ordinal); }
    }

    [Fact]
    public void Assignment_ReacquiresEveryNativeFunctionAfterCommit()
    {
        int generation = 0;
        bool assigned = false;
        var page = new Page();
        var project = new Project(page);
        EplanProjects.Project = project;
        project.FindFunctions = _ =>
        {
            int created = generation;
            Action guard = () => Assert.Equal(created, generation);
            var target = new Function(1) { Name = "-K1", Page = page, Pins = assigned ? [new() { Name = "13" }] : [], CheckNative = guard };
            var source = new Function(2) { Name = "-K1", Pins = [new() { Name = "13" }], CheckNative = guard };
            source.OnAssign = actual => { guard(); Assert.Equal(1, actual.ObjectIdentifier); assigned = true; };
            return assigned ? [target] : [source, target];
        };
        FakeMoveNative.EndTransaction = () => generation++;
        try
        {
            EplanFunctionAssignment.Result result = EplanFunctionAssignment.Assign("project", 2, 1, "-K1");
            Assert.Null(result.Warning);
            Assert.Equal(["13"], result.Pins!);
            Assert.Empty(result.UnplacedFunctions!);
        }
        finally { FakeMoveNative.EndTransaction = null; }
    }
}
