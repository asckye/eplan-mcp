using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Borrowed fakes deliberately reject disposal.
[Trait("Size", "Medium")]
public sealed class PlacementRemoverTests
{
    [Fact]
    public void PreparedDeletionDoesNotWriteUntilInvoked()
    {
        var page = new Page();
        var line = new Line { Page = page };
        var text = new Text { Page = page };
        page.AllPlacements = [line, text];
        EplanProjects.Project = new(page);
        Action change = new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(line)]);
        Assert.Equal(2, page.AllPlacements.Length);
        change();
        Assert.Same(text, Assert.Single(page.AllPlacements));
    }

    [Fact]
    public void ExecutionResolvesFreshNativeWrappersInsteadOfRetainingPreviewObjects()
    {
        var page = new Page();
        var preview = new Line(1) { Page = page };
        page.AllPlacements = [preview];
        EplanProjects.Project = new(page);
        Action remove = new EplanPlacementRemover().Prepare("project", page.Name, [Locator(preview)]);
        var execution = new Line(1) { Page = page };
        page.AllPlacements = [execution];
        remove();
        Assert.Empty(page.AllPlacements);
    }

    [Fact]
    public void UnreadableBlockOnThePageDoesNotStopDeletingAnotherGraphic()
    {
        // Live 2026-11-04: =K1+A2&EFA1/5 holds an empty nested block whose SubPlacements throws.
        var page = new Page();
        var line = new Line { Page = page };
        var block = new Block { SubPlacementsError = new BaseException("access") };
        page.AllPlacements = [line, block];
        EplanProjects.Project = new(page);

        new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(line)])();

        Assert.Same(block, Assert.Single(page.AllPlacements));
    }

    [Theory]
    [InlineData("Function")]
    [InlineData("Terminal")]
    [InlineData("InterruptionPoint")]
    [InlineData("BoxedDevice")]
    [InlineData("PLC")]
    public void FunctionKindsCanBeRemovedNatively(string kind)
    {
        var page = new Page();
        Function function = kind switch
        {
            "Terminal" => new Terminal(),
            "InterruptionPoint" => new InterruptionPoint(),
            "BoxedDevice" => new BoxedDevice(),
            "PLC" => new PLC(),
            _ => new Function()
        };
        function.Page = page;
        page.AllPlacements = [function];
        EplanProjects.Project = new(page);

        Action remove = new EplanPlacementRemover().Prepare("project", page.Name, [Locator(function)]);

        Assert.Same(function, Assert.Single(page.AllPlacements));
        remove();
        Assert.Empty(page.AllPlacements);
    }

    [Fact]
    public void BlockMembersAreRefusedNatively()
    {
        var page = new Page();
        var line = new Line { Page = page };
        var block = new Block();
        block.Create(page, [line, new Line(2)]);
        page.AllPlacements = [line, block];
        EplanProjects.Project = new(page);
        Assert.Contains("members", Assert.Throws<BridgeErrorException>(() => new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(line)])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PropertyTextAndAutoconnectAreRefusedNatively()
    {
        var page = new Page();
        var property = new PropertyPlacement { Page = page };
        var connection = new DynamicConnectionLine(2) { Page = page };
        page.AllPlacements = [property, connection];
        EplanProjects.Project = new(page);

        BridgeError propertyError = Assert.Throws<BridgeErrorException>(() => new EplanPlacementRemover().Prepare("project", page.Name, [Locator(property)])).Error;
        BridgeError connectionError = Assert.Throws<BridgeErrorException>(() => new EplanPlacementRemover().Prepare("project", page.Name, [Locator(connection)])).Error;

        Assert.Equal("Invalid arguments: Block members and property text cannot be deleted separately.", propertyError.Message);
        Assert.Equal("Invalid arguments: Cannot delete DynamicConnectionLine: devices, connections and blocks are excluded.", connectionError.Message);
        Assert.Equal(ErrorReason.InvalidArgument, propertyError.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, connectionError.Reason);
    }

    [Fact]
    public void NestedGroupMembershipDoesNotPreventDeletingLine()
    {
        var page = new Page();
        var line = new Line { Page = page, Group = new Group(999) { Group = new Group(998) } };
        page.AllPlacements = [line];
        EplanProjects.Project = new(page);
        Action remove = new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(line)]);
        Assert.Single(page.AllPlacements);
        remove();
        Assert.Empty(page.AllPlacements);
    }
    private sealed class InterruptionPoint : Function
    {
    }
    private static DrawingLocator Locator(Placement placement) => new() { Type = placement.IdentifierType, Oid = placement.ObjectIdentifier };
}
#pragma warning restore CA2000
