using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A3/A14/A39/A52-A54: bounded to one page; borrowed objects are never disposed, no property lists are acquired.</summary>
internal sealed class EplanDrawingReader : IPageDrawingReader
{
    public PageDrawingGeometry? Read(string projectPath, string pageName)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        Page? page = finder.GetPages(filter).SingleOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
        if (page is null)
        {
            return null;
        }

        var result = new PageDrawingGeometry
        {
            Page = page.Name,
            GridSize = DrawingCoordinates.ApiToMillimetres(page.GridSize),
            PageSize = Optional(() => XY(page.Size)),
            PlotFrameSize = page.PlotFrame is { } frame ? Optional(() => XY(frame.Size)) : null,
        };
        DrawingCoordinates.Snap(0, result.GridSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Walk(IEnumerable<Placement> placements, bool excluded, DrawingLocator? parent = null)
        {
            foreach (Placement placement in placements)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if ((placement is PropertyPlacement && parent is null && !excluded) || !seen.Add(placement.ToStringIdentifier()))
                {
                    continue;
                }

                DrawingPlacement? row = !excluded && (placement is SymbolReference or PropertyPlacement || placement.GetType() == typeof(Text)
                    || placement.GetType() == typeof(Line) || placement.GetType() == typeof(Rectangle)) ? ReadPlacement(placement, parent) : null;
                if (row is not null)
                {
                    result.Placements.Add(row);
                }

                Walk(placement switch
                {
                    Group group => group.SubPlacements,
                    SymbolReference symbol => symbol.PropertyPlacements,
                    IPropertyPlacementsContainer container => container.PropertyPlacements,
                    _ => [],
                }, excluded, row?.Locator);
            }
        }

        DrawingPlacement ReadPlacement(Placement placement, DrawingLocator? parent)
        {
            // A53/A54: property Location is relative, GetBoundingBox(false) is absolute.
            PointD origin = placement is PropertyPlacement property && property.Parent is Placement owner ? owner.Location : new PointD(0, 0);
            double[] location = XY(new PointD(placement.Location.X + origin.X, placement.Location.Y + origin.Y));
            double[]? bounds = Optional(() => (placement is PropertyPlacement p ? p.GetBoundingBox(false) : placement.GetBoundingBox()).SelectMany(XY).ToArray());
            if (!DrawingCoordinates.ValidBounds(bounds))
            {
                bounds = null;
            }

            using MultiLangString? contents = placement switch { PropertyPlacement p => p.Contents, Text t => t.Contents, _ => null };
            string digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(new
            {
                kind = placement.GetType().Name,
                parentType = parent?.Type,
                parentOid = parent?.Oid,
                location,
                bounds,
                content = contents?.GetAsString(),
                name = (placement as FunctionBase)?.Name,
                display = (placement as PropertyPlacement)?.GetDisplayString(),
                start = placement is Line line ? XY(line.StartPoint) : null,
                end = placement is Line endLine ? XY(endLine.EndPoint) : null,
            }));
            return new DrawingPlacement
            {
                Properties = new Dictionary<string, string?> { ["name"] = (placement as FunctionBase)?.Name, ["text"] = contents?.GetAsString(), ["display"] = (placement as PropertyPlacement)?.GetDisplayString() }
                    .Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
                Kind = placement.GetType().Name,
                Location = location,
                Bounds = bounds,
                Parent = parent,
                Locator = DrawingLocators.FromIdentifier(projectPath, page.Name, placement.ToStringIdentifier(), digest),
            };
        }

        if (page.PlotFrame is { } plotFrame)
        {
            Walk(plotFrame.SubPlacements, excluded: true);
        }

        Walk(page.AllPlacements, excluded: false);
        return result;
    }

    private static double[] XY(PointD point) => [DrawingCoordinates.ApiToMillimetres(point.X), DrawingCoordinates.ApiToMillimetres(point.Y)];
    private static double[]? Optional(Func<double[]> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        {
            return null; // A14/S42: unavailable size or default empty property text bounds.
        }
    }
}
