using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanPlacementTextWriter
{
    private static Func<PlacementText> Annotation(string projectPath, string page, PlacementText request,
        Func<GraphicalPlacement, Func<PlacementText>, Func<PlacementText>> styled)
    {
        Page target = Find(projectPath, page);
        PointD Point(double[] xy) => new(DrawingCoordinates.MillimetresToApi(xy[0]), DrawingCoordinates.MillimetresToApi(xy[1]));
        double[] XY(PointD p) => [DrawingCoordinates.ApiToMillimetres(p.X), DrawingCoordinates.ApiToMillimetres(p.Y)];
#pragma warning disable CA2000 // Project-owned placements live within the caller's transaction (A55).
        switch (request.Kind)
        {
            case "hyperlink":
                using (var contents = new MultiLangString())
                {
                    foreach (var entry in request.Contents)
                    { using var code = new ISOCode(entry.Key); contents.AddString(entry.Key == "??_??" ? ISOCode.Language.L___ : code.GetNumber(), entry.Value); }
                    var link = new HyperLink();
                    link.Create(target, request.Link!, contents);
                    link.Location = Point(request.Position);
                    return styled(link, () =>
                    {
                        PlacementText actual = Read(link);
                        actual.Kind = "hyperlink";
                        actual.HeightMm = 0;
                        actual.Link = link.Link;
                        return actual;
                    });
                }
            case "qr_code":
                var qr = new QRCode();
                qr.Create(target, Point(request.Position), DrawingCoordinates.MillimetresToApi(request.SizeMm), request.Text!);
                return styled(qr, () => new() { Kind = request.Kind, Position = XY(qr.Location), SizeMm = DrawingCoordinates.ApiToMillimetres(qr.Width), Text = qr.GetQRCode() });
            case "spline":
                var spline = new Spline();
                spline.Create(target);
                for (int i = 0; i < request.Points.Length; i++)
                { PointD point = Point(request.Points[i]); spline.SetPointAt(i, ref point); }
                return styled(spline, () => new() { Kind = request.Kind, Position = XY(spline.GetPointAt(0)), Points = Enumerable.Range(0, spline.NumberOfPoints).Select(i => XY(spline.GetPointAt(i))).ToArray() });
            case "dimension" when request.DimensionType is "linear" or "angular":
                var dimension = new DimensionItem();
                if (request.DimensionType == "linear")
                { dimension.Create(target, Point(request.Points[0]), Point(request.Points[1]), Point(request.EndPosition)); }
                else
                { dimension.CreateAngular(target, Point(request.Points[0]), Point(request.Points[1]), Point(request.EndPosition)); }
                return styled(dimension, () => new()
                {
                    Kind = request.Kind,
                    DimensionType = request.DimensionType,
                    Position = XY(dimension.Location),
                    Points = [XY(dimension.FirstPoint), XY(dimension.SecondPoint)],
                    EndPosition = XY(dimension.EndPoint),
                    DimensionText = [dimension.DimensionText]
                });
            case "dimension" when request.DimensionType == "circle":
                var circleDimension = new DimensionCircle();
                circleDimension.Create(target, Circle(projectPath, page, request.Arc!), Point(request.EndPosition));
                return styled(circleDimension, () => new() { Kind = request.Kind, DimensionType = request.DimensionType, Position = XY(circleDimension.Location), DimensionText = [circleDimension.DimensionText], Arc = request.Arc });
            case "dimension":
                var group = new DimensionGroup();
                var type = request.DimensionType switch { "baseline" => DimensionGroup.Enums.DimensionType.BaseLine, "incremental" => DimensionGroup.Enums.DimensionType.Incremental, _ => DimensionGroup.Enums.DimensionType.Continued };
                group.Create(target, request.Points.Select(Point).ToArray(), Point(request.EndPosition), type);
                DimensionItem[] children = group.SubPlacements.OfType<DimensionItem>().ToArray();
                return () => new() { Kind = request.Kind, DimensionType = request.DimensionType, Position = XY(group.Location), DimensionText = children.Select(c => c.DimensionText).ToArray() };
            default:
                throw new BridgeErrorException(ErrorReason.InvalidArgument, "Unsupported annotation.");
        }
#pragma warning restore CA2000
    }

    private static Arc Circle(string projectPath, string page, DrawingLocator locator)
    {
        if (!ProjectResolver.SamePath(locator.Project, projectPath) || locator.Page != page)
        { throw WriteArguments.Invalid(["/arc must identify an Arc on the target page."]); }
        // Re-resolve inside each call/transaction; no native Arc escapes validation into creation.
        Arc? arc = Find(projectPath, page).AllPlacements.OfType<Arc>().SingleOrDefault(a => a.ObjectIdentifier == locator.Oid && SnapshotCanonical.TypeOf(a.ToStringIdentifier()) == locator.Type);
        if (arc is null)
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Arc no longer exists; find it again."); }
        string digest = SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(EplanObjectReader.Describe(arc, "arc")));
        if (digest != locator.Digest)
        { throw new BridgeErrorException(ErrorReason.PlanStale, "Arc changed; find it again."); }
        if (arc.Kind.ToString() != "Circle")
        { throw WriteArguments.Invalid(["/arc must have Arc.Kind=Circle."]); }
        return arc;
    }
}
