using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // A39: fake native functions and pages belong to the project; disposing borrowed objects is forbidden.

[Trait("Size", "Small")]
public sealed class DevicePartFunctionAssignmentTests
{
    [Fact]
    public void A98_ExistingFourContactsKeepTheirPinsWhenPartTemplatesAreAssigned()
    {
        Reset();
        var page = new Page();
        var project = new Project(page);
        string[][] pins = [["1/L1", "2/T1"], ["3/L2", "4/T2"], ["5/L3", "6/T3"], ["13", "14"]];
        Function[] placed = [.. pins.Select((names, i) => Function(i + 2, page, names))];
        page.AllPlacements = [new Function(1) { Name = "+A1-K1", Page = page, IsMainFunction = true, FunctionCategory = FunctionCategory.Contactor }, .. placed];
        project.UnplacedFunctions = [.. pins.Select((names, i) => Function(i + 12, null, names))];
        foreach (Function source in project.UnplacedFunctions)
        {
            source.OnAssign = target =>
            {
                Assert.Equal(source.Pins.Select(p => p.Name), target.Pins.Select(p => p.Name));
                project.UnplacedFunctions = [.. project.UnplacedFunctions.Where(f => f != source)];
            };
        }
        EplanProjects.Project = project;
        var result = new EplanDevicePartAssigner().AssignUnplacedFunctions("project", "+A1-K1");
        Assert.Equal(4, result.Count);
        Assert.Null(result.Warning);
        Assert.Empty(project.UnplacedFunctions);
        Assert.Equal(5, page.AllPlacements.Length);
        Assert.Equal(pins.SelectMany(p => p), placed.SelectMany(f => f.Pins.Select(p => p.Name)));
        Assert.Equal(4, FakeMoveNative.Calls.Count(c => c == "Commit"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("reversed")]
    [InlineData("different")]
    [InlineData("category")]
    [InlineData("main")]
    public void NonmatchingTemplatesLeaveThePlacedFunctionUntouched(string mismatch)
    {
        Reset();
        var page = new Page();
        Function target = Function(2, page, ["13", "14"]);
        Function source = Function(5, null, mismatch == "reversed" ? ["14", "13"] : mismatch == "different" ? ["23", "24"] : ["13", "14"]);
        target.IsMainFunction = mismatch == "main";
        if (mismatch == "category")
        { source.FunctionCategory = FunctionCategory.Contactor; }
        page.AllPlacements = [target];
        EplanProjects.Project = new Project(page) { UnplacedFunctions = mismatch == "none" ? [] : [source] };
        source.OnAssign = _ => Assert.Fail("A nonmatching function must not be assigned.");
        var result = new EplanDevicePartAssigner().AssignUnplacedFunctions("project", "+A1-K1");
        Assert.Equal(0, result.Count);
        Assert.Null(result.Warning);
        Assert.Equal(["13", "14"], target.Pins.Select(p => p.Name));
        Assert.DoesNotContain("Transaction", FakeMoveNative.Calls);
    }

    [Fact]
    public void MatchingSourcesAndTargetsUseAscendingObjectIdentifiers()
    {
        Reset();
        var page = new Page();
        Function first = Function(2, page, ["13", "14"]), second = Function(3, page, ["13", "14"]);
        Function low = Function(10, null, ["13", "14"]), high = Function(20, null, ["13", "14"]);
        page.AllPlacements = [second, first];
        var project = new Project(page) { UnplacedFunctions = [high, low] };
        var assignments = new List<(long Source, long Target)>();
        foreach (Function source in project.UnplacedFunctions)
        {
            source.OnAssign = target =>
            {
                assignments.Add((source.ObjectIdentifier, target.ObjectIdentifier));
                project.UnplacedFunctions = [.. project.UnplacedFunctions.Where(f => f != source)];
            };
        }
        EplanProjects.Project = project;
        Assert.Equal(2, new EplanDevicePartAssigner().AssignUnplacedFunctions("project", "+A1-K1").Count);
        Assert.Equal([(10L, 2L), (20L, 3L)], assignments);
    }

    [Fact]
    public void SourceRetainedAfterAssignmentWarnsAndIsNotAssignedAgain()
    {
        Reset();
        var page = new Page();
        Function first = Function(2, page, ["13", "14"]), second = Function(3, page, ["13", "14"]);
        Function source = Function(10, null, ["13", "14"]);
        page.AllPlacements = [first, second];
        EplanProjects.Project = new Project(page) { UnplacedFunctions = [source] };
        int calls = 0;
        source.OnAssign = _ => calls++;
        var result = new EplanDevicePartAssigner().AssignUnplacedFunctions("project", "+A1-K1");
        Assert.Equal(1, result.Count);
        Assert.Equal(1, calls);
        Assert.Contains("source remains unplaced", result.Warning, StringComparison.Ordinal);
        Assert.Single(EplanProjects.Project.UnplacedFunctions);
    }

    [Fact]
    public void UnreadableCategoryWarnsWithoutRefusingPartAssignment()
    {
        Reset();
        var page = new Page();
        Function target = Function(2, page, ["13", "14"]), source = Function(5, null, ["13", "14"]);
        source.CategoryReadError = new InvalidOperationException("Native category is unreadable.");
        page.AllPlacements = [target];
        EplanProjects.Project = new Project(page) { UnplacedFunctions = [source] };
        var result = new EplanDevicePartAssigner().AssignUnplacedFunctions("project", "+A1-K1");
        Assert.Equal(0, result.Count);
        Assert.Contains("could not be checked", result.Warning, StringComparison.Ordinal);
    }

    private static Function Function(long id, Page? page, string[] pins) => new(id)
    {
        Name = "+A1-K1",
        Page = page!,
        IsPlaced = page is not null,
        FunctionCategory = FunctionCategory.NoContact,
        Pins = [.. pins.Select(name => new Pin { Name = name })]
    };

    private static void Reset()
    {
        FakeMoveNative.Calls.Clear();
        FakeMoveNative.EndTransaction = null;
    }
}

#pragma warning restore CA2000
