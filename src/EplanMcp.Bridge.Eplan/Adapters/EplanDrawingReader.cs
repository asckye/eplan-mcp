using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
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
            GridSize = Optional(() => [page.GridSize])?[0] ?? 0,
            PageSize = Optional(() => XY(page.Size)),
            PlotFrameSize = Optional(() => page.PlotFrame is { } frame ? XY(frame.Size) : null),
        };
        if (double.IsNaN(result.GridSize) || double.IsInfinity(result.GridSize) || result.GridSize <= 0)
        { result.GridSize = 0; OperationContext.Current.Warn("Page grid is unavailable; grid checks remain unverified."); }
        if (result.PageSize is not { Length: 2 } || result.PageSize.Any(n => double.IsNaN(n) || double.IsInfinity(n) || n <= 0))
        { result.PageSize = null; OperationContext.Current.Warn("Page size is unavailable; boundary checks remain unverified."); }
        if (result.PlotFrameSize is not { Length: 2 } || result.PlotFrameSize.Any(n => double.IsNaN(n) || double.IsInfinity(n) || n <= 0))
        { result.PlotFrameSize = null; }
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
        void Walk(IEnumerable<Placement> placements, bool excluded, DrawingLocator? parent = null, bool blockChild = false)
        {
            int index = 0;
            foreach (Placement placement in placements)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                // Live T2b run: Function.PropertyPlacements are ContextPropertyPlacements that share the function's
                // identifier, so key them by parent, displayed property and position, not by ToStringIdentifier.
                string? slot = null;
                if (placement is PropertyPlacement child && parent is not null)
                {
                    string? propertyId;
                    try
                    { propertyId = Number(DisplayedId(child)); }
                    catch (Exception ex) when (ex is BaseException or InvalidOperationException)
                    { WriteExceptionDiagnostics.Report(ex); propertyId = null; }
                    slot = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}#{1}", propertyId, index++);
                }
                // Live 2026-11-04: all members of a block report one identifier, so only their position tells them apart.
                bool member = slot is null && blockChild && parent is not null;
                string key = slot is not null ? FormattableString.Invariant($"{parent!.Type}/{parent.Oid}#{slot}")
                    : member ? FormattableString.Invariant($"{parent!.Type}/{parent.Oid}@{index++}")
                    : placement.ToStringIdentifier();
                if ((placement is PropertyPlacement && parent is null && !excluded) || !seen.Add(key))
                {
                    continue;
                }
                if (member)
                { seen.Add(placement.ToStringIdentifier()); }

                DrawingPlacement? row = !excluded && (placement is SymbolReference or PropertyPlacement or Block || placement.GetType() == typeof(Text)
                    || placement is PathText or PolyLine or Arc or Image or HyperLink or QRCode or DimensionItem or DimensionCircle or PlaceHolder or MacroBox || placement.GetType() == typeof(Line) || placement.GetType() == typeof(Rectangle)) ? ReadPlacement(placement, parent, slot, blockChild) : null;
                if (row is not null)
                {
                    result.Placements.Add(row);
                }

                IEnumerable<Placement> children;
                try
                {
                    children = placement switch
                    {
                        Group group => group.SubPlacements,
                        Block block => block.SubPlacements,
                        SymbolReference symbol => symbol.PropertyPlacements,
                        IPropertyPlacementsContainer container => container.PropertyPlacements,
                        _ => [],
                    };
                }
                catch (Exception ex) when (ex is BaseException or InvalidOperationException)
                {
                    WriteExceptionDiagnostics.Report(ex);
                    if (row is not null)
                    { row.Properties["read_warning"] = "Child placements are unavailable; inspect this placement in EPLAN."; }
                    children = [];
                }
                Walk(children, excluded, row?.Locator, blockChild || placement is Block);
            }
        }

        DrawingPlacement ReadPlacement(Placement placement, DrawingLocator? parent, string? slot, bool blockChild)
        {
            bool metadataUnavailable = false;
            T ReadOptional<T>(Func<T> read)
            {
                try
                { return read(); }
                catch (Exception ex) when (ex is BaseException or InvalidOperationException)
                { WriteExceptionDiagnostics.Report(ex); metadataUnavailable = true; return default!; }
            }
            // A87: Location and default bounds are relative; GetBoundingBox(false) is absolute.
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
            string? display = ReadOptional(() => (placement as PropertyPlacement)?.GetDisplayString());
            if (bounds is null && display == "")
            { bounds = [0, 0, 0, 0]; } // A53/A54: empty property text has no visible ink.

            using MultiLangString? contents = ReadOptional(() => placement switch { PropertyPlacement p => p.Contents, HyperLink h => h.Contents, Text t => t.Contents, _ => null });
            string? text = ReadOptional(() => contents?.GetAsString());
            string? angle = ReadOptional(() => placement is TextBase angleText ? EplanTextFormat.Angle(angleText.Rotation) : null);
            string? height = ReadOptional(() => placement is PathText { Height: -16002 } ? "-16002"
                : placement is TextBase heightText ? EplanTextFormat.Height(heightText.Height) : null);
            string? name = ReadOptional(() => placement is Block namedBlock ? namedBlock.Name : placement is PlaceHolder holder ? holder.Name : (placement as FunctionBase)?.Name);
            int? count = ReadOptional(() => placement is Block countedBlock ? countedBlock.Count : (int?)null);
            long? group = ReadOptional(() => (long?)placement.Group?.ObjectIdentifier);
            bool? grouped = ReadOptional(() => (bool?)(placement.Group is not null));
            int? drawingOrder = ReadOptional(() => (int?)placement.DrawingOrder);
            double? rotation = ReadOptional(() => (placement as PropertyPlacement)?.Rotation);
            bool? docked = ReadOptional(() => (placement as PropertyPlacement)?.IsDocked);
            bool? visible = ReadOptional(() => (placement as PropertyPlacement)?.IsVisible);
            string? displayedId = ReadOptional(() => placement is PropertyPlacement property ? DisplayedId(property).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            string? rawHeight = ReadOptional(() => placement is PropertyPlacement raw ? Number(raw.Height) : null);
            double[]? start = ReadOptional(() => placement is Line line ? XY(line.StartPoint) : null);
            double[]? end = ReadOptional(() => placement is Line line ? XY(line.EndPoint) : null);
            string? imagePath = null;
            Dictionary<string, string?> graphics;
            try
            {
                graphics = placement switch
                {
                    HyperLink hyperlink => new() { ["link"] = hyperlink.Link },
                    // Live 2026-11-04: GetQRCode and DimensionText after the commit make EPLAN's undo fail with "step does not exist".
                    QRCode code => new() { ["text"] = QrValue(code) },
                    MacroBox macro => new() { ["macro_name"] = macro.MacroName },
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
                if (placement is Rectangle rectangle)
                { angle = EplanTextFormat.Angle(rectangle.Angle); }
                if (placement is GraphicalPlacement graphical)
                {
                    Pen pen = graphical.Pen;
                    foreach (var pair in new Dictionary<string, string?>
                    {
                        ["layer"] = graphical.LayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["pen_color"] = Number(pen.ColorId),
                        ["pen_width_mm"] = Number(pen.Width),
                        ["pen_style"] = Number(pen.StyleId),
                        ["pen_pattern_length"] = Number(pen.PatternLength),
                        ["pen_line_end"] = Number(pen.LineEndType),
                        ["alignment"] = (placement as TextBase)?.Justification.ToString()
                    })
                    { graphics[pair.Key] = pair.Value; }
                }
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); graphics["read_warning"] = "Style metadata unavailable; inspect this placement in EPLAN."; }
            // A75 catalog: DynamicConnectionLine derives from SymbolReference (DMClassId.InternalTypeId = 33),
            // but its wire outline is neither a layout symbol nor a container.
            // Live 2026-11-01: a placed terminal strip definition has no symbol variant; it is never a container.
            bool isContainer = ReadOptional(() => placement is not DynamicConnectionLine && (placement is LocationBox || placement is SymbolReference container && (container.SymbolVariant?.SymbolName == "PLCC"
                || placement.GetType().Name == "PLC" || placement is Function boxFunction && boxFunction.FunctionCategory.ToString() is "PLCBox" or "BlackBox" or "Blackbox")));
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
            if (metadataUnavailable)
            { graphics["read_warning"] = "Some placement metadata is unavailable; inspect this placement in EPLAN."; }
            string digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(new
            {
                kind = placement is PropertyPlacement ? nameof(PropertyPlacement) : placement.GetType().Name,
                parentType = parent?.Type,
                parentOid = parent?.Oid,
                containerOwner,
                slot, // property placements share their parent's identifier
                rotation,
                docked,
                group,
                drawingOrder,
                location,
                bounds,
                content = text,
                height,
                angle,
                name,
                count,
                display,
                visible,
                start,
                end,
                graphics,
                imageFile = imagePath,
            }));
            return new DrawingPlacement
            {
                Properties = new Dictionary<string, string?>
                {
                    ["coordinate_space"] = blockChild ? "block-relative; absolute geometry unverified" : null,
                    ["layout_symbol"] = placement is SymbolReference && placement.GetType().Name is not ("LocationBox" or "Cable" or "DynamicConnectionLine") ? "true" : null,
                    ["symbol"] = placement is SymbolReference reference ? SymbolOf(reference) : null,
                    ["layout_container_owner"] = containerOwner,
                    ["layout_container"] = isContainer ? "true" : null,
                    ["grouped"] = grouped?.ToString().ToLowerInvariant(),
                    ["name"] = name,
                    ["count"] = count is { } knownCount ? Number(knownCount) : null,
                    ["text"] = text,
                    ["display"] = display,
                    ["property_id"] = displayedId,
                    ["displayed_property_id"] = displayedId,
                    ["property_index"] = slot?.Split('#')[1],
                    ["angle_deg"] = placement is PropertyPlacement ? angle : null,
                    ["docked"] = docked?.ToString().ToLowerInvariant(),
                    ["cross_reference"] = displayedId is "20300" or "20314" or "20304" or "20252" or "20306" or "20302" or "24300" ? "true" : null,
                    ["angle"] = angle,
                    ["visible"] = visible?.ToString().ToLowerInvariant(),
                    ["height_mm"] = height,
                    ["height_raw_mm"] = rawHeight
                }
                    .Concat(graphics).Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal),
                Kind = placement is PropertyPlacement ? nameof(PropertyPlacement) : placement.GetType().Name, // ContextPropertyPlacement too
                Location = blockChild ? [] : location,
                Bounds = blockChild ? null : bounds,
                DrawingOrder = drawingOrder,
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

        Walk(EplanNativePreconditions.Valid(native).OrderByDescending(p => p is Block).ToArray(), excluded: false); // A13: include returned objects which are not yet visible to page enumeration.
        return result;
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
