using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // Fake placements are borrowed, matching native ownership.

[Trait("Size", "Small")]
public sealed class DynamicConnectionLineLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicConnectionLine_IsNeitherLayoutSymbolNorContainer(bool containerVariant)
    {
        var wire = new DynamicConnectionLine(423455)
        {
            IdentifierType = 33,
            Location = new(120, 160),
            Bounds = [new(120, 160), new(200, 160)]
        };
        if (containerVariant)
        { wire.SymbolVariant.Name = "PLCC"; }
        var page = new Page { Name = "=GAA+A1&EFS1/103", AllPlacements = [wire] };
        EplanProjects.Project = new(page);
        try
        {
            DrawingPlacement row = Assert.Single(new EplanDrawingReader().Read("project", page.Name)!.Placements);
            Assert.Equal("DynamicConnectionLine", row.Kind);
            Assert.Equal((33, 423455L), (row.Locator.Type, row.Locator.Oid));
            Assert.Equal(new double[] { 120, 160, 200, 160 }, row.Bounds);
            Assert.False(row.Properties.ContainsKey("layout_symbol"));
            Assert.False(row.Properties.ContainsKey("layout_container"));
            Assert.False(row.Properties.ContainsKey("layout_container_owner"));
        }
        finally { EplanProjects.Project = null; }
    }
}
