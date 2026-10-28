using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    private DevicePlaceData CreateLocationBox(DevicePlaceArgs args)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(_project);
        var before = finder.GetFunctions(null).Select(f => (Function: f, Name: f.Name)).ToArray();
        // A55: use the native box factory, never a graphical rectangle substitute.
#pragma warning disable CA2000 // A39/A55: native placements belong to EPLAN, also after Abort.
        SymbolReference created = SymbolReference.Create(_variant, _page);
#pragma warning restore CA2000
        Require(created is LocationBox);
        var box = (LocationBox)created;
        box.SetLogicalArea(new PointD(args.X, args.Y), new PointD(args.CornerX!.Value, args.CornerY!.Value));
        using (var parts = new FunctionBasePropertyList())
        { parts.DESIGNATION_LOCATION = args.LocationBox!.Substring(1); box.NameParts = parts; }
        GraphicalPlacement[] graphics = [.. BoxGraphics(box.GetGraphics())];
        Require(graphics.Length > 0);
        PointD size = _page.Size;
        foreach (GraphicalPlacement graphic in graphics)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            Pen pen = graphic.Pen;
            pen.StyleId = 1; // S43/A55: installed style 1 is dashed.
            graphic.Pen = pen;
            PointD[] bounds = graphic.GetBoundingBox();
            Require(graphic.Pen.StyleId == 1 && bounds.Length >= 2 && bounds.All(p =>
                !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y)
                && p.X >= 0 && p.Y >= 0 && p.X <= size.X && p.Y <= size.Y));
        }
        using FunctionBasePropertyList actual = box.NameParts;
        using PropertyValue location = actual.DESIGNATION_LOCATION;
        string structure = location.IsEmpty ? string.Empty : "+" + location.ToString();
        RectangleD area = box.GetLogicalArea();
        Require(structure == args.LocationBox && box.Page.Name == _page.Name
            && area.LowerLeft.X == args.X && area.LowerLeft.Y == args.Y && area.UpperRight.X == args.CornerX && area.UpperRight.Y == args.CornerY
            && box.SymbolVariant.SymbolLibraryName == _symbol.Library && box.SymbolVariant.SymbolName == _symbol.Name && box.SymbolVariant.VariantNr == _symbol.Variant
            && before.All(f => f.Function.Name == f.Name));
        return new DevicePlaceData
        {
            Page = _page.Name,
            Tag = structure,
            Category = "LocationBox",
            Functions = 0,
            LocationBox = new() { Structure = structure, Bounds = [area.LowerLeft.X, area.LowerLeft.Y, area.UpperRight.X, area.UpperRight.Y] }
        };
    }

    private static IEnumerable<GraphicalPlacement> BoxGraphics(Placement? root)
    {
        if (root is GraphicalPlacement graphic)
        { yield return graphic; }
        if (root is Group group)
        { foreach (Placement child in group.SubPlacements) { foreach (GraphicalPlacement item in BoxGraphics(child)) { yield return item; } } }
    }
}
