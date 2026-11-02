using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

internal static class LayoutFixture
{
    // These tests supply trusted detached geometry; native prediction availability is tested separately.
    internal static MacroTargetPage? Target(MacroTargetPage? page)
    {
        if (page is null)
        { return null; }
        page.Layout ??= new() { Page = new() { GridSize = 4, PageSize = [page.Width ?? 1000, page.Height ?? 1000] }, Config = new() { MarginMm = 0 } };
        page.Layout.ReadActual ??= () => page.Layout;
        page.PredictDevice ??= args => [LayoutGuard.Proposed(args.LocationBox is null ? "Function" : "LocationBox", [args.X, args.Y],
            [args.X, args.Y, args.CornerX ?? args.X, args.CornerY ?? args.Y])];
        return page;
    }
}
