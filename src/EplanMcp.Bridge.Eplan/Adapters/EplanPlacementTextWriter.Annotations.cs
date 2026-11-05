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
            default:
                throw new BridgeErrorException(ErrorReason.InvalidArgument, "Unsupported annotation.");
        }
#pragma warning restore CA2000
    }

}
