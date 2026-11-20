using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanDrawingReader
{
    /// <summary>
    /// One read of a page: the page measures first, then the frame's placements (seen, not reported) and the page's own
    /// placements with their children. The native reads keep the order they were verified in; see the notes at each step.
    /// </summary>
    private sealed class Reading(string projectPath, Page page, IReadOnlyList<Placement> created)
    {
        private readonly PageDrawingGeometry _result = new()
        {
            Page = page.Name,
            Project = projectPath,
            GridSize = Optional(() => [page.GridSize])?[0] ?? 0,
            PageSize = Optional(() => XY(page.Size)),
            PlotFrameSize = Optional(() => page.PlotFrame is { } frame ? XY(frame.Size) : null),
        };
        private readonly Dictionary<string, string> _cardOwners = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private bool _metadataUnavailable;
        // Where a read spends its time, by step; one line in the add-in log per page review.
        private readonly Dictionary<string, long> _ticks = new(StringComparer.Ordinal);
        private void Add(string step, long ticks) => _ticks[step] = (_ticks.TryGetValue(step, out long sum) ? sum : 0) + ticks;
        private T Timed<T>(string step, Func<T> read)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            { return read(); }
            finally
            { Add(step, System.Diagnostics.Stopwatch.GetTimestamp() - start); }
        }

        public PageDrawingGeometry Run()
        {
            CheckSizes();
            ReadDrawingArea();
            Placement[] native = Timed("all", () => (Placement[])[.. page.AllPlacements, .. created]);
            Timed("cards", () => { ReadCardOwners(native); return 0; });
            try
            {
                if (page.PlotFrame is { } plotFrame)
                { Timed("frame", () => { Walk(plotFrame.SubPlacements, excluded: true); return 0; }); }
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            {
                // A49: unavailable frame geometry uses the page-size boundary fallback. The frame's members were not all
                // marked as seen, so the page's own placements may include some of them.
                WriteExceptionDiagnostics.Report(ex);
                OperationContext.Current.Warn("The plot frame's members could not all be read; the placements may include frame graphics.");
            }

            Walk(EplanNativePreconditions.Valid(native).OrderByDescending(p => p is Block).ToArray(), excluded: false); // A13: include returned objects which are not yet visible to page enumeration.
            LastProfile = string.Join("; ", _ticks.OrderByDescending(p => p.Value).Select(p => FormattableString.Invariant($"{p.Key} {p.Value * 1000 / System.Diagnostics.Stopwatch.Frequency}")));
            return _result;
        }

        private void CheckSizes()
        {
            if (double.IsNaN(_result.GridSize) || double.IsInfinity(_result.GridSize) || _result.GridSize <= 0)
            { _result.GridSize = 0; OperationContext.Current.Warn("Page grid is unavailable; grid checks remain unverified."); }
            if (_result.PageSize is not { Length: 2 } || _result.PageSize.Any(n => double.IsNaN(n) || double.IsInfinity(n) || n <= 0))
            { _result.PageSize = null; OperationContext.Current.Warn("Page size is unavailable; boundary checks remain unverified."); }
            if (_result.PlotFrameSize is not { Length: 2 } || _result.PlotFrameSize.Any(n => double.IsNaN(n) || double.IsInfinity(n) || n <= 0))
            { _result.PlotFrameSize = null; }
        }

        private void ReadDrawingArea()
        {
            _result.DrawingArea = Optional(() => page.PlotFrame is not null ? FrameArea(page) : null);
            // A145: a scaled page (official =EC1+A3&EFS1/1: 630 x 445.5 with a 420 x 297 frame) draws the frame grid at page scale.
            if (_result.DrawingArea is { Length: 4 } grid && _result.PageSize is { Length: 2 } page2 && _result.PlotFrameSize is { Length: 2 } frame2)
            { _result.DrawingArea = [grid[0] * page2[0] / frame2[0], grid[1] * page2[1] / frame2[1], grid[2] * page2[0] / frame2[0], grid[3] * page2[1] / frame2[1]]; }
            if (_result.DrawingArea is not { Length: 4 } a || _result.PageSize is not { Length: 2 } s || a.Any(n => double.IsNaN(n) || double.IsInfinity(n))
                || a[0] < 0 || a[1] < 0 || a[2] > s[0] + 1e-6 || a[3] > s[1] + 1e-6 || a[0] >= a[2] || a[1] >= a[3])
            { _result.DrawingArea = null; }
            // Batch 2 (owner 2026-11-16): the column edges of the frame grid, at page scale, for the column checks.
            _result.ColumnEdges = _result.DrawingArea is null ? null : Optional(() => FrameColumns(page));
            if (_result.ColumnEdges is { Length: > 1 } edges && _result.PageSize is { Length: 2 } pageSize && _result.PlotFrameSize is { Length: 2 } frameSize)
            { _result.ColumnEdges = [.. edges.Select(e => e * pageSize[0] / frameSize[0])]; }
            if (_result.ColumnEdges is not { Length: > 2 } columns || columns.Zip(columns.Skip(1), (a, b) => b - a).Any(w => !(w > 0)))
            { _result.ColumnEdges = null; }
        }

        private void ReadCardOwners(Placement[] native)
        {
            // A55: Terminal.PLC may be null even when PLCTerminals reports the card membership.
            foreach (PLC card in native.OfType<PLC>().GroupBy(p => p.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()))
            {
                try
                { foreach (Terminal terminal in card.PLCTerminals ?? []) { _cardOwners[terminal.ToStringIdentifier()] = card.ToStringIdentifier(); } }
                catch (Exception ex) when (ex is BaseException or InvalidOperationException)
                { WriteExceptionDiagnostics.Report(ex); /* Unreadable membership grants no collision exemption. */ }
            }
        }

        private void Walk(IEnumerable<Placement> placements, bool excluded, DrawingLocator? parent = null, bool blockChild = false)
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
                // A90, Live 2026-11-04: all members of a block report one identifier, so only their position tells them apart.
                bool member = slot is null && blockChild && parent is not null;
                string key = slot is not null ? FormattableString.Invariant($"{parent!.Type}/{parent.Oid}#{slot}")
                    : member ? FormattableString.Invariant($"{parent!.Type}/{parent.Oid}@{index++}")
                    : Timed("identifier", placement.ToStringIdentifier);
                if ((placement is PropertyPlacement && parent is null && !excluded) || !_seen.Add(key))
                {
                    continue;
                }
                if (member)
                { _seen.Add(placement.ToStringIdentifier()); }

                DrawingPlacement? row = !excluded && (placement is SymbolReference or PropertyPlacement or Block || placement.GetType() == typeof(Text)
                    || placement is PathText or PolyLine or Arc or Image or HyperLink or QRCode or DimensionItem or DimensionCircle or PlaceHolder or MacroBox || placement.GetType() == typeof(Line) || placement.GetType() == typeof(Rectangle)) ? Timed(excluded ? "row.frame" : placement is PropertyPlacement ? "row.property" : placement is SymbolReference ? "row.symbol" : "row.graphic", () => ReadPlacement(placement, parent, slot, blockChild)) : null;
                if (row is not null)
                {
                    _result.Placements.Add(row);
                }

                IEnumerable<Placement> children;
                try
                {
                    children = Timed<IEnumerable<Placement>>("children", () => placement switch
                    {
                        Group group => group.SubPlacements,
                        Block block => block.SubPlacements,
                        SymbolReference symbol => symbol.PropertyPlacements,
                        IPropertyPlacementsContainer container => container.PropertyPlacements,
                        _ => [],
                    });
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

        private DrawingPlacement ReadPlacement(Placement placement, DrawingLocator? parent, string? slot, bool blockChild)
        {
            _metadataUnavailable = false;
            double[] location = Timed("location", () => ReadLocation(placement));
            string? display = Timed("display", () => ReadOptional(() => (placement as PropertyPlacement)?.GetDisplayString()));
            // A53/A54: empty property text has no visible ink. A168, live 2026-11-19 (=EC1+A3&EFS1/1): 2416 of its 3239
            // property texts display nothing; EPLAN gave each a point or no outline, and asking took half of the page read.
            double[]? bounds = display == "" ? [0, 0, 0, 0] : Timed("bounds", () => ReadBounds(placement));

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
            // Corpus 2026-11-17 (official PLC sample =K3+B2&EFS/1, A147): the property texts of a macro box read IsVisible false on
            // the first read of the page and true on later reads; they carry no ink either way, and the digest must not change.
            bool? visible = ReadOptional(() => placement is PropertyPlacement { Parent: Placement owner } && owner.GetType().Name == "MacroBox" ? null
                : (placement as PropertyPlacement)?.IsVisible);
            string? displayedId = ReadOptional(() => placement is PropertyPlacement property ? DisplayedId(property).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            string? rawHeight = ReadOptional(() => placement is PropertyPlacement raw ? Number(raw.Height) : null);
            double[]? start = ReadOptional(() => placement is Line line ? XY(line.StartPoint) : null);
            double[]? end = ReadOptional(() => placement is Line line ? XY(line.EndPoint) : null);
            Dictionary<string, string?> graphics = ReadGraphics(placement, out string? imagePath);
            ReadStyle(placement, graphics, ref angle);
            // A75 catalog: DynamicConnectionLine derives from SymbolReference (DMClassId.InternalTypeId = 33),
            // but its wire outline is neither a layout symbol nor a container.
            // A158, Live 2026-11-01: a placed terminal strip definition has no symbol variant; it is never a container.
            // A147, Corpus 2026-11-17: SPECIAL/DC is the black box symbol; the official repair switch -QB1 (HVAC EFS/30) uses it on a
            // function whose category is not BlackBox, and its wires run into the box to the contacts inside.
            bool isContainer = Timed("container", () => ReadOptional(() => IsContainer(placement)));
            string? containerOwner = Timed("owner", () => ContainerOwner(placement, isContainer));
            if (_metadataUnavailable)
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
                    ["symbol"] = placement is SymbolReference reference ? Timed("symbolOf", () => SymbolOf(reference)) : null,
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
                Locator = Locator(placement, parent, digest),
            };
        }

        private static double[] ReadLocation(Placement placement)
        {
            // A87: Location and default bounds are relative; GetBoundingBox(false) is absolute.
            return Optional(() =>
            {
                PointD origin = placement is PropertyPlacement property && property.Parent is Placement owner ? owner.Location : new PointD(0, 0);
                return XY(new PointD(placement.Location.X + origin.X, placement.Location.Y + origin.Y));
            }) ?? [];
        }

        private static double[]? ReadBounds(Placement placement)
        {
            double[]? bounds = Optional(() => Outline(placement));
            if (!DrawingCoordinates.ValidBounds(bounds))
            {
                bounds = null;
            }
            // A146, Live 2026-11-16 (official =K2+B2.Y1&MTC1/1): a model view part reports no outline on the first read of the page
            // and a point at its insertion point on the next. Both carry no outline, and the digest must not change between
            // reads, or a drawing cursor goes stale.
            if (bounds is { Length: 4 } point && placement.GetType().Name == "ViewPart" && point[0] == point[2] && point[1] == point[3])
            {
                bounds = null;
            }
            return bounds;
        }

        private DrawingLocator Locator(Placement placement, DrawingLocator? parent, string digest) => placement is PropertyPlacement && parent is not null
                ? new DrawingLocator { Project = projectPath, Page = page.Name, Type = parent.Type, Oid = parent.Oid, Digest = digest }
                : DrawingLocators.FromIdentifier(projectPath, page.Name, placement.ToStringIdentifier(), digest);

        private T ReadOptional<T>(Func<T> read)
        {
            try
            { return read(); }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); _metadataUnavailable = true; return default!; }
        }

        private static double[] Outline(Placement placement)
        {
            if (placement is BoxedDevice device)
            { RectangleD area = device.GetLogicalArea(); return new[] { area.LowerLeft, area.UpperRight }.SelectMany(XY).ToArray(); }
            if (placement is LocationBox box)
            { RectangleD area = box.GetLogicalArea(); return new[] { area.LowerLeft, area.UpperRight }.SelectMany(XY).ToArray(); }
            return (placement is PropertyPlacement p ? EplanTextBounds.Of(p) : placement.GetBoundingBox()).SelectMany(XY).ToArray();
        }

        private Dictionary<string, string?> ReadGraphics(Placement placement, out string? imagePath)
        {
            imagePath = null;
            try
            {
                return placement switch
                {
                    HyperLink hyperlink => new() { ["link"] = hyperlink.Link },
                    // A99, Live 2026-11-04: GetQRCode and DimensionText after the commit make EPLAN's undo fail with "step does not exist".
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
                    Image image => new() { ["file"] = Path.GetFileName(imagePath = EplanPaths.Substitute(image.FileName, projectPath)) },
                    _ => [],
                };
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); return new() { ["read_warning"] = "Graphics metadata unavailable; inspect this placement in EPLAN." }; }
        }

        private static void ReadStyle(Placement placement, Dictionary<string, string?> graphics, ref string? angle)
        {
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
        }

        private static bool IsContainer(Placement placement) =>
            placement is not DynamicConnectionLine && (placement is LocationBox || placement is SymbolReference container && (container.SymbolVariant?.SymbolName is "PLCC" or "DC"
                || placement.GetType().Name == "PLC" || placement is Function boxFunction && boxFunction.FunctionCategory.ToString() is "PLCBox" or "BlackBox" or "Blackbox"));

        private string? ContainerOwner(Placement placement, bool isContainer)
        {
            string? containerOwner = isContainer ? placement.ToStringIdentifier() : null;
            try
            {
                if (_cardOwners.TryGetValue(placement.ToStringIdentifier(), out string? card))
                { containerOwner = card; }
                else if (placement is Function function && function.ParentFunction is { } box && box.FunctionCategory.ToString() is "PLCBox" or "BlackBox" or "Blackbox")
                { containerOwner = box.ToStringIdentifier(); }
            }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { WriteExceptionDiagnostics.Report(ex); /* Unreadable ownership grants no collision exemption. */ }
            return containerOwner;
        }
    }
}
