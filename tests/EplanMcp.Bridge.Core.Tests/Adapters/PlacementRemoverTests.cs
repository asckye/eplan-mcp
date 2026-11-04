using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Borrowed fakes deliberately reject disposal.
[Trait("Size", "Small")]
public sealed class PlacementRemoverTests
{
    [Fact]
    public void PreparedDeletionDoesNotWriteUntilInvoked()
    {
        var page = new Page(); var line = new Line { Page = page }; var text = new Text { Page = page };
        page.AllPlacements = [line, text]; EplanProjects.Project = new(page);
        Action change = new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(line)]);
        Assert.Equal(2, page.AllPlacements.Length);
        change();
        Assert.Same(text, Assert.Single(page.AllPlacements));
    }

    [Fact]
    public void FunctionAndGroupedLineAreRefusedNatively()
    {
        var page = new Page(); var function = new Function { Page = page };
        page.AllPlacements = [function]; EplanProjects.Project = new(page);
        Assert.Contains("Function", Assert.Throws<BridgeErrorException>(() => new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(function)])).Message, StringComparison.Ordinal);
        var line = new Line { Page = page, Group = new Group(999) }; page.AllPlacements = [line];
        Assert.Contains("members", Assert.Throws<BridgeErrorException>(() => new EplanPlacementRemover().Prepare("project", "+A1/1", [Locator(line)])).Message, StringComparison.Ordinal);
    }
    private static DrawingLocator Locator(Placement placement) => new() { Type = placement.IdentifierType, Oid = placement.ObjectIdentifier };
}
#pragma warning restore CA2000
