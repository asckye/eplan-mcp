using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A3/A14/A39/A52-A54: bounded to one page; borrowed objects are never disposed, no property lists are acquired.</summary>
internal sealed partial class EplanDrawingReader : IPageDrawingReader
{
    public PageDrawingGeometry? Read(string projectPath, string pageName) => Read(projectPath, pageName, []);
    internal static PageDrawingGeometry? Read(string projectPath, string pageName, IReadOnlyList<Placement> created)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        Page? page = EplanNativePreconditions.Valid(finder.GetPages(filter)).ToArray().SingleOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
        if (page is null)
        {
            return null;
        }

        return new Reading(projectPath, page, created).Run();
    }

    private static string? QrValue(QRCode code)
    {
        using QRCodePropertyList properties = code.Properties;
        using PropertyValue value = properties.QRCODE_BLOCK_VALUE;
        return value.IsEmpty ? null : value.ToString();
    }

    private static int DisplayedId(PropertyPlacement placement)
    {
        using AnyPropertyId id = placement.DisplayedProperty;
        return id.AsInt;
    }

    /// <summary>
    /// A145: the evaluation grid starts at 12003/12004; its height is the sum of the row heights 12002 and its width the sum
    /// of the column widths 12102. A frame that stores no widths spans the frame width 12033 from its start.
    /// </summary>
    /// <summary>Batch 2: x of every column edge, from 12003 across the column widths 12102 in index order; null without widths.</summary>
    private static double[]? FrameColumns(Page page)
    {
        using var properties = page.PlotFrame!.Properties;
        using PropertyValue start = properties.FRAME_EVALUATION_AREA_START_POINT_X;
        using PropertyValue widths = properties.FRAME_PATHSIZE;
        if (start.IsEmpty)
        { return null; }
        var edges = new List<double> { start.ToDouble() };
        foreach (int index in widths.Indexes.OrderBy(i => i))
        {
            using PropertyValue item = widths[index];
            if (!item.IsEmpty)
            { edges.Add(edges[edges.Count - 1] + item.ToDouble()); }
        }
        return edges.Count > 1 ? [.. edges] : null;
    }

    private static double[]? FrameArea(Page page)
    {
        using var properties = page.PlotFrame!.Properties;
        static double Value(PropertyValue value)
        { using (value) { return value.IsEmpty ? double.NaN : value.ToDouble(); } }
        static double Sum(PropertyValue value)
        {
            using (value)
            {
                double total = 0;
                foreach (int index in value.Indexes)
                {
                    using PropertyValue item = value[index];
                    if (!item.IsEmpty)
                    { total += item.ToDouble(); }
                }
                return total;
            }
        }
        double x = Value(properties.FRAME_EVALUATION_AREA_START_POINT_X), y = Value(properties.FRAME_EVALUATION_AREA_START_POINT_Y);
        double width = Sum(properties.FRAME_PATHSIZE), height = Sum(properties.FRAME_POSITIONSIZE);
        if (width <= 0)
        { width = Value(properties.FRAME_AREA_X) - x; }
        if (height <= 0)
        { height = Value(properties.FRAME_AREA_Y) - y; }
        return [x, y, x + width, y + height];
    }

    private static double[] XY(PointD point) => [DrawingCoordinates.ApiToMillimetres(point.X), DrawingCoordinates.ApiToMillimetres(point.Y)];
    /// <summary>A114: library/name/variant of the placed symbol, what a redraw needs to place the same one; null when EPLAN cannot tell.</summary>
    private static string? SymbolOf(SymbolReference reference)
    {
        try
        {
            var variant = reference.SymbolVariant;
            return variant is null ? null : variant.SymbolLibraryName + "/" + variant.SymbolName + "/" + variant.VariantNr.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (BaseException ex)
        {
            WriteExceptionDiagnostics.Report(ex);
            return null;
        }
    }

    private static string Number(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private static double[]? Optional(Func<double[]?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException or BridgeErrorException)
        {
            WriteExceptionDiagnostics.Report(ex);
            return null; // A14/S42: unavailable size or default empty property text bounds.
        }
    }
}
