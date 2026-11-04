using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A3/A14/A39/A52-A54: bounded to one page; borrowed objects are never disposed, no property lists are acquired.</summary>
internal sealed class EplanDrawingReader : IPageDrawingReader
{
    public PageDrawingGeometry? Read(string projectPath, string pageName) => Read(projectPath, pageName, []);
    internal PageDrawingGeometry? Read(string projectPath, string pageName, IReadOnlyList<Placement> created)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter { Name = pageName, ExactNameMatching = true };
        Page? page = EplanNativePreconditions.Valid(finder.GetPages(filter)).ToArray().SingleOrDefault(p => string.Equals(p.Name, pageName, StringComparison.Ordinal));
        if (page is null)
        {
            return null;
        }

        var result = new PageDrawingGeometry
        {
            Page = page.Name,
            Project = projectPath,
            GridSize = DrawingCoordinates.ApiToMillimetres(page.GridSize),
            PageSize = Optional(() => XY(page.Size)),
            PlotFrameSize = Optional(() => page.PlotFrame is { } frame ? XY(frame.Size) : null),
        };
        DrawingCoordinates.Snap(0, result.GridSize);
        Placement[] native = [.. page.AllPlacements, .. created];
        var cardOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        // A55: Terminal.PLC may be null even when PLCTerminals reports the card membership.
        foreach (PLC card in native.OfType<PLC>().GroupBy(p => p.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()))
        {
            try
            { foreach (Terminal terminal in card.PLCTerminals ?? []) { cardOwners[terminal.ToStringIdentifier()] = card.ToStringIdentifier(); } }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { /* Unreadable membership grants no collision exemption. */ }
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Walk(IEnumerable<Placement> placements, bool excluded, DrawingLocator? parent = null)
        {
            int index = 0;
            foreach (Placement placement in placements)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                // Live T2b run: Function.PropertyPlacements are ContextPropertyPlacements that share the function's
                // identifier, so key them by parent, displayed property and position, not by ToStringIdentifier.
                string? slot = placement is PropertyPlacement child && parent is not null
                    ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}#{1}", DisplayedId(child), index++)
                    : null;
                string key = slot is null ? placement.ToStringIdentifier() : FormattableString.Invariant($"{parent!.Type}/{parent.Oid}#{slot}");
                if ((placement is PropertyPlacement && parent is null && !excluded) || !seen.Add(key))
                {
                    continue;
                }

                DrawingPlacement? row = !excluded && (placement is SymbolReference or PropertyPlacement || placement.GetType() == typeof(Text)
                    || placement is PathText or PolyLine or Arc or Image || placement.GetType() == typeof(Line) || placement.GetType() == typeof(Rectangle)) ? ReadPlacement(placement, parent, slot) : null;
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

        DrawingPlacement ReadPlacement(Placement placement, DrawingLocator? parent, string? slot)
        {
            // A53/A54: property Location is relative, GetBoundingBox(false) is absolute.
            double[] location = Optional(() =>
            {
                PointD origin = placement is PropertyPlacement property && property.Parent is Placement owner ? owner.Location : new PointD(0, 0);
                return XY(new PointD(placement.Location.X + origin.X, placement.Location.Y + origin.Y));
            }) ?? [];
            double[]? bounds = Optional(() =>
            {
                if (placement is BoxedDevice device)
                { RectangleD area = device.GetLogicalArea(); return new[] { area.LowerLeft, area.UpperRight }.SelectMany(XY).ToArray(); }
                if (placement is LocationBox box)
                { RectangleD area = box.GetLogicalArea(); return new[] { area.LowerLeft, area.UpperRight }.SelectMany(XY).ToArray(); }
                return (placement is PropertyPlacement p ? p.GetBoundingBox(false) : placement.GetBoundingBox()).SelectMany(XY).ToArray();
            });
            if (!DrawingCoordinates.ValidBounds(bounds))
            {
                bounds = null;
            }
            string? display = (placement as PropertyPlacement)?.GetDisplayString();
            if (bounds is null && display == "")
            { bounds = [0, 0, 0, 0]; } // A53/A54: empty property text has no visible ink.

            using MultiLangString? contents = placement switch { PropertyPlacement p => p.Contents, Text t => t.Contents, _ => null };
            string? angle = placement is TextBase angleText ? EplanTextFormat.Angle(angleText.Rotation) : placement is Rectangle rectangle ? EplanTextFormat.Angle(rectangle.Angle) : null;
            string? height = placement is PathText { Height: -16002 } ? "-16002"
                : placement is TextBase heightText ? EplanTextFormat.Height(heightText.Height) : null;
            string? imagePath = null;
            Dictionary<string, string?> graphics;
            try
            {
                graphics = placement switch
                {
                    PolyLine polyline => new() { ["points"] = JsonConvert.SerializeObject(Enumerable.Range(0, polyline.NumberOfPoints).Select(i => XY(polyline.GetPointAt(i)))), ["closed"] = polyline.Closed.ToString().ToLowerInvariant() },
                    Arc arc => new()
                    {
                        ["radius_mm"] = Number(DrawingCoordinates.ApiToMillimetres(arc.Radius)),
                        ["start_angle"] = Number(arc.StartAngle * (180 / Math.PI)),
                        ["end_angle"] = Number(arc.EndAngle * (180 / Math.PI)),
                        ["sector"] = arc.Sector.ToString().ToLowerInvariant(),
                        ["arc_kind"] = arc.Kind.ToString(),
                        ["radius_x_mm"] = arc.Kind.ToString() == "Ellipse" ? Number(DrawingCoordinates.ApiToMillimetres(arc.FirstRadius)) : null,
                        ["radius_y_mm"] = arc.Kind.ToString() == "Ellipse" ? Number(DrawingCoordinates.ApiToMillimetres(arc.SecondRadius)) : null,
                        ["ellipse_angle"] = arc.Kind.ToString() == "Ellipse" ? Number(arc.ElipseAngle) : null
                    },
                    Image image => new() { ["file"] = Path.GetFileName(imagePath = PathMap.SubstitutePath(image.FileName, projectPath)) },
                    _ => [],
                };
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); graphics = new() { ["read_warning"] = "Graphics metadata unavailable; inspect this placement in EPLAN." }; }
            try
            {
                if (placement is GraphicalPlacement graphical)
                {
                    Pen pen = graphical.Pen;
                    foreach (var pair in new Dictionary<string, string?> { ["layer"] = graphical.LayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["pen_color"] = Number(pen.ColorId), ["pen_width_mm"] = Number(pen.Width), ["pen_style"] = Number(pen.StyleId),
                        ["pen_pattern_length"] = Number(pen.PatternLength), ["pen_line_end"] = Number(pen.LineEndType),
                        ["alignment"] = (placement as TextBase)?.Justification.ToString() }) { graphics[pair.Key] = pair.Value; }
                }
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); graphics["read_warning"] = "Style metadata unavailable; inspect this placement in EPLAN."; }
            // A75 catalog: DynamicConnectionLine derives from SymbolReference (DMClassId.InternalTypeId = 33),
            // but its wire outline is neither a layout symbol nor a container.
            // Live 2026-11-01: a placed terminal strip definition has no symbol variant; it is never a container.
            bool isContainer = placement is not DynamicConnectionLine && (placement is LocationBox || placement is SymbolReference container && (container.SymbolVariant?.SymbolName == "PLCC"
                || placement.GetType().Name == "PLC" || placement is Function boxFunction && boxFunction.FunctionCategory.ToString() is "PLCBox" or "BlackBox" or "Blackbox"));
            string? containerOwner = isContainer ? placement.ToStringIdentifier() : null;
            try
            {
                if (cardOwners.TryGetValue(placement.ToStringIdentifier(), out string? card))
                { containerOwner = card; }
                else if (placement is Function function && function.ParentFunction is { } box && box.FunctionCategory.ToString() is "PLCBox" or "BlackBox" or "Blackbox")
                { containerOwner = box.ToStringIdentifier(); }
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { /* Unreadable ownership grants no collision exemption. */ }
            string digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(new
            {
                kind = placement is PropertyPlacement ? nameof(PropertyPlacement) : placement.GetType().Name,
                parentType = parent?.Type,
                parentOid = parent?.Oid,
                containerOwner,
                slot, // property placements share their parent's identifier
                rotation = (placement as PropertyPlacement)?.Rotation,
                docked = (placement as PropertyPlacement)?.IsDocked,
                group = placement.Group?.ObjectIdentifier,
                location,
                bounds,
                content = contents?.GetAsString(),
                height,
                angle,
                name = (placement as FunctionBase)?.Name,
                display,
                visible = placement is PropertyPlacement visibleProperty ? visibleProperty.IsVisible : (bool?)null,
                start = placement is Line line ? XY(line.StartPoint) : null,
                end = placement is Line endLine ? XY(endLine.EndPoint) : null,
                graphics,
                imageFile = imagePath,
            }));
            return new DrawingPlacement
            {
                Properties = new Dictionary<string, string?>
                {
                    ["layout_symbol"] = placement is SymbolReference && placement.GetType().Name is not ("LocationBox" or "Cable" or "DynamicConnectionLine") ? "true" : null,
                    ["layout_container_owner"] = containerOwner,
                    ["layout_container"] = isContainer ? "true" : null,
                    ["grouped"] = (placement.Group is not null).ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant(),
                    ["name"] = (placement as FunctionBase)?.Name,
                    ["text"] = contents?.GetAsString(),
                    ["display"] = display,
                    ["property_id"] = placement is PropertyPlacement propertyText ? DisplayedId(propertyText).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    ["displayed_property_id"] = placement is PropertyPlacement displayed ? DisplayedId(displayed).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    ["property_index"] = slot?.Split('#')[1],
                    ["angle_deg"] = placement is PropertyPlacement ? angle : null,
                    ["docked"] = placement is PropertyPlacement docked ? docked.IsDocked.ToString().ToLowerInvariant() : null,
                    ["cross_reference"] = placement is PropertyPlacement crossText && DisplayedId(crossText) is 20300 or 20314 or 20304 or 20252 or 20306 or 20302 or 24300 ? "true" : null,
                    ["angle"] = angle,
                    ["visible"] = placement is PropertyPlacement visible ? visible.IsVisible.ToString(System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant() : null,
                    ["height_mm"] = height
                }
                    .Concat(graphics).Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
                Kind = placement is PropertyPlacement ? nameof(PropertyPlacement) : placement.GetType().Name, // ContextPropertyPlacement too
                Location = location,
                Bounds = bounds,
                Parent = parent,
                // A57: default library texts become local context copies when edited. Identify both by
                // their parent and slot/content digest so copying does not change the drawing identity.
                Locator = placement is PropertyPlacement && parent is not null
                    ? new DrawingLocator { Project = projectPath, Page = page.Name, Type = parent.Type, Oid = parent.Oid, Digest = digest }
                    : DrawingLocators.FromIdentifier(projectPath, page.Name, placement.ToStringIdentifier(), digest),
            };
        }

        try
        {
            if (page.PlotFrame is { } plotFrame)
            { Walk(plotFrame.SubPlacements, excluded: true); }
        }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        { /* A49: unavailable frame geometry uses the page-size boundary fallback. */ }

        Walk(EplanNativePreconditions.Valid(native).ToArray(), excluded: false); // A13: include returned objects which are not yet visible to page enumeration.
        return result;
    }

    private static int DisplayedId(PropertyPlacement placement)
    {
        using AnyPropertyId id = placement.DisplayedProperty;
        return id.AsInt;
    }

    private static double[] XY(PointD point) => [DrawingCoordinates.ApiToMillimetres(point.X), DrawingCoordinates.ApiToMillimetres(point.Y)];
    private static string Number(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private static double[]? Optional(Func<double[]?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is BaseException or InvalidOperationException)
        {
            WriteExceptionDiagnostics.Report(ex);
            return null; // A14/S42: unavailable size or default empty property text bounds.
        }
    }
}
