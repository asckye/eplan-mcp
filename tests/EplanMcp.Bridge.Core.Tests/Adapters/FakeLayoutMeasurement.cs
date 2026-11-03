using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal static class EplanLayout
{
    internal static LayoutGuard.Model Read(string project, string page) => Measure(project, page, []);
    internal static IReadOnlyList<EplanMcp.Contracts.DrawingPlacement> Device(string project, EplanMcp.Contracts.DevicePlaceArgs args, LayoutGuard.Model layout) => [];
    internal static LayoutGuard.Model Measure(string project, string page, IReadOnlyList<Placement> created)
    {
        Assert.Equal("project", project);
        Assert.All(created, placement => Assert.Equal(page, placement.Page.Name));
        return new() { Page = new() { Page = page } };
    }
}
