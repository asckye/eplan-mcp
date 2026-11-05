using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDeviceSymbolPlacement
{
    private DevicePlaceData CreateLocationBox(DevicePlaceArgs args, Project project, Page page, SymbolVariant variant)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(project);
        var before = EplanNativePreconditions.Valid(finder.GetFunctions(null)).ToArray().Select(f => (Function: f, Name: f.Name)).ToArray();
        // A55: use the native box factory, never a graphical rectangle substitute.
#pragma warning disable CA2000 // A39/A55: native placements belong to EPLAN, also after Abort.
        SymbolReference created = SymbolReference.Create(variant, page);
#pragma warning restore CA2000
        Check(created is LocationBox, "factory did not return a location box");
        var box = (LocationBox)created;
        box.SetLogicalArea(new PointD(args.X, args.Y), new PointD(args.CornerX!.Value, args.CornerY!.Value));
        using (var parts = new FunctionBasePropertyList())
        {
            // Customer page: like the editor, the box takes the page's higher-level function (=GAA+P1), so it shows +P1, not
            // =+P1. Live 2026-11-02: '=' is DESIGNATION_PLANT; DESIGNATION_FUNCTIONALASSIGNMENT is '==' and left the box =+P1.
            if (DrawingNames.HigherLevelFunctionOf(page.Name) is { Length: > 0 } function)
            { parts.DESIGNATION_PLANT = function; }
            parts.DESIGNATION_LOCATION = args.LocationBox!.Substring(1);
            box.NameParts = parts;
        }
        EplanVisibleNames.Adjust(page, box);
        GraphicalPlacement[] graphics = [.. BoxGraphics(box.GetGraphics())];
        Check(graphics.Length > 0, "the box exposes no graphics");
        foreach (GraphicalPlacement graphic in graphics)
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            Pen pen = graphic.Pen;
            pen.StyleId = 1; // S43/A55: installed style 1 is dashed.
            graphic.Pen = pen;
            PointD[] bounds = graphic.GetBoundingBox();
            Check(graphic.Pen.StyleId == 1, "dashed pen did not read back");
            // Live T7a: graphic bounds are relative to the box (0,-40;40,0); the page extent is the logical area below.
            Check(bounds.Length >= 2 && bounds.All(p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y)),
                "outline is not finite");
        }
        using FunctionBasePropertyList actual = box.NameParts;
        using PropertyValue location = actual.DESIGNATION_LOCATION;
        string structure = location.IsEmpty ? string.Empty : "+" + location.ToString();
        RectangleD area = box.GetLogicalArea();
        Check(structure == args.LocationBox, $"structure read back as '{structure}'");
        Check(box.Page.Name == page.Name, "box is on another page");
        Check(area.LowerLeft.X == args.X && area.LowerLeft.Y == args.Y && area.UpperRight.X == args.CornerX && area.UpperRight.Y == args.CornerY,
            FormattableString.Invariant($"area read back as {area.LowerLeft.X},{area.LowerLeft.Y} - {area.UpperRight.X},{area.UpperRight.Y}"));
        Check(box.SymbolVariant.SymbolLibraryName == _symbol.Library && box.SymbolVariant.SymbolName == _symbol.Name && box.SymbolVariant.VariantNr == _symbol.Variant,
            $"symbol read back as {box.SymbolVariant.SymbolLibraryName}/{box.SymbolVariant.SymbolName}/{box.SymbolVariant.VariantNr}");
        Check(before.All(f => f.Function.Name == f.Name), "an existing device was renamed");
        if (args.PropertyArrangement is { } arrangement)
        { EplanSymbolArrangement.Select(box, arrangement); }
        using UniversalPropertyList? values = args.PropertyArrangement is null ? null : box.Properties;
        _placedLayout = EplanLayout.Measure(_projectPath, _pageName, [box]);
        return new DevicePlaceData
        {
            PropertyArrangement = args.PropertyArrangement is null ? null : EplanSymbolArrangement.SelectedName(box),
            ActivePropertySet = args.PropertyArrangement is null ? null : EplanSymbolArrangement.ReadActive(values!),
            Page = page.Name,
            Tag = structure,
            Category = "LocationBox",
            Functions = 0,
            LocationBox = new() { Structure = structure, Bounds = [area.LowerLeft.X, area.LowerLeft.Y, area.UpperRight.X, area.UpperRight.Y] }
        };
    }

    private DevicePlaceData CreateBlackBox(DevicePlaceArgs args, string tag, Page page, SymbolVariant variant)
    {
#pragma warning disable CA2000 // A86: persistent boxes belong to EPLAN, including on Abort.
        var box = new BoxedDevice();
#pragma warning restore CA2000
        // A86: the symbol can only be set on a created object (S063080 before Create).
        box.Create(page);
        box.SymbolVariant = variant;
        if (args.JoinDevice == true)
        { box.IsMainFunction = false; }
        box.Name = tag;
        box.SetLogicalArea(new PointD(args.X, args.Y), new PointD(args.CornerX!.Value, args.CornerY!.Value));
        var category = box.FunctionCategory;
        Require(box.IsMainFunction == (args.JoinDevice != true) && category.ToString() == "Blackbox" && DevicePlaceRequest.SameName(tag, box.Name));
        RectangleD area = box.GetLogicalArea();
        Require(area.LowerLeft.X == args.X && area.LowerLeft.Y == args.Y && area.UpperRight.X == args.CornerX && area.UpperRight.Y == args.CornerY);
        if (args.PropertyArrangement is { } arrangement)
        { EplanSymbolArrangement.Select(box, arrangement); }
        using UniversalPropertyList? values = args.PropertyArrangement is null ? null : box.Properties;
        _placedFunctionId = box.ObjectIdentifier;
        _placedLayout = EplanLayout.Measure(_projectPath, _pageName, [box]);
        return new() { PropertyArrangement = args.PropertyArrangement is null ? null : EplanSymbolArrangement.SelectedName(box), ActivePropertySet = args.PropertyArrangement is null ? null : EplanSymbolArrangement.ReadActive(values!), Page = page.Name, Tag = box.Name, Category = category.ToString(), Functions = 1, IsMainFunction = args.JoinDevice == true ? box.IsMainFunction : null };
    }

    private void ReadSpecialCommitted(DevicePlaceArgs args, DevicePlaceData data)
    {
        Page page = EplanProjects.ByPath(_projectPath).Pages.Single(p => p.Name == _pageName);
        Function[] functions = EplanNativePreconditions.Valid(page.AllPlacements).OfType<Function>().ToArray();
        Function placed = functions.Single(f => f.ObjectIdentifier == _placedFunctionId);
        string expectedTag = data.Tag;
        data.Tag = placed.Name;
        data.Type = placed.GetType().Name;
        data.Category = placed.FunctionCategory.ToString();
        data.IsMainFunction = placed.IsMainFunction;
        Require(placed.IsPlaced && placed.Page.Name == data.Page && placed.SymbolVariant.SymbolLibraryName == _symbol.Library
            && placed.SymbolVariant.SymbolName == _symbol.Name && placed.SymbolVariant.VariantNr == _symbol.Variant
            && (data.Category == _symbol.FunctionCategory || args.JoinDevice == true && _symbol.FunctionCategory is null));
        if (args.JoinDevice == true)
        { Require(data.IsMainFunction == false && DevicePlaceRequest.SameName(expectedTag, placed.Name)); }
        if (DeviceSymbolPlan.BusBarDefinition(args))
        { Require(data.IsMainFunction == (args.JoinDevice != true) && data.Category == "BusBarDefText"); }
        BoxedDevice? box = placed as BoxedDevice ?? functions.OfType<BoxedDevice>().FirstOrDefault(b => b.FunctionsInside.Any(f => f.ObjectIdentifier == placed.ObjectIdentifier));
        if (box is null)
        { return; }
        RectangleD area = box.GetLogicalArea();
        data.BlackBox = new() { Tag = box.Name, Bounds = [area.LowerLeft.X, area.LowerLeft.Y, area.UpperRight.X, area.UpperRight.Y], FunctionsInside = box.FunctionsInside.Select(f => f.Name).ToArray() };
        if (DeviceSymbolPlan.BlackBox(args))
        { Require(data.Type == "BoxedDevice" && data.IsMainFunction == (args.JoinDevice != true) && data.BlackBox.Bounds.SequenceEqual(new[] { args.X, args.Y, args.CornerX!.Value, args.CornerY!.Value })); }
    }

    private static void Check(bool matches, string what)
    {
        if (!matches)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Location box readback differs (" + what + "). Nothing was committed; read the page and re-plan."); }
    }

    private static IEnumerable<GraphicalPlacement> BoxGraphics(Placement? root)
    {
        if (root is GraphicalPlacement graphic)
        { yield return graphic; }
        if (root is Group group)
        { foreach (Placement child in group.SubPlacements) { foreach (GraphicalPlacement item in BoxGraphics(child)) { yield return item; } } }
    }
}
