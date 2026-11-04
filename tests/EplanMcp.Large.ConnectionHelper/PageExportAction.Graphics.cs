using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

public sealed partial class PageExportAction
{
    private static bool ReadGraphics(Placement placement, JObject row)
    {
        if (placement is not (Line or Rectangle or PolyLine or Arc or TextBase))
        { return false; }
        if (placement is GraphicalPlacement graphical)
        {
            Read(row, "layer", graphical, "GraphicalPlacement.LayerId", () => graphical.LayerId);
            Pen? pen = Get<Pen?>(row, graphical, "GraphicalPlacement.Pen", () => graphical.Pen);
            if (pen is not null)
            {
                var penRow = new JObject();
                row["pen"] = penRow;
                Read(penRow, "color", pen, "Pen.ColorId", () => pen.ColorId);
                Read(penRow, "width", pen, "Pen.Width", () => pen.Width);
                Read(penRow, "style", pen, "Pen.StyleId", () => pen.StyleId);
                Read(penRow, "styleFactor", pen, "Pen.StyleFactor", () => pen.StyleFactor);
                Read(penRow, "patternLength", pen, "Pen.PatternLength", () => pen.PatternLength);
                Read(penRow, "lineEndType", pen, "Pen.LineEndType", () => pen.LineEndType);
                GC.KeepAlive(graphical);
            }
        }

        switch (placement)
        {
            case PropertyPlacement property:
                ReadPropertyPlacement(property, row);
                ReadText(property, row);
                return true;
            case Image image:
                Read(row, "fileName", image, "Image.FileName", () => image.FileName);
                ReadRectangle(image, row);
                return true;
            case Line line:
                Read(row, "start", line, "Line.StartPoint", () => Point(line.StartPoint));
                Read(row, "end", line, "Line.EndPoint", () => Point(line.EndPoint));
                return true;
            case Rectangle rectangle:
                ReadRectangle(rectangle, row);
                return true;
            case PolyLine polyline:
                var points = new JArray();
                var bulges = new JArray();
                row["points"] = points;
                row["bulges"] = bulges;
                int count = Get(row, polyline, "PolyLine.NumberOfPoints", () => polyline.NumberOfPoints);
                for (int index = 0; index < count; index++)
                {
                    int pointIndex = index;
                    JObject? point = Get<JObject?>(row, polyline, "PolyLine.GetPointAt[" + index + "]", () => Point(polyline.GetPointAt(pointIndex)));
                    points.Add(point is null ? JValue.CreateNull() : point);
                    JToken? bulge = Get<JToken?>(row, polyline, "PolyLine.GetBulgeAt[" + index + "]", () => JToken.FromObject(polyline.GetBulgeAt(pointIndex)));
                    bulges.Add(bulge ?? JValue.CreateNull());
                }
                Read(row, "closed", polyline, "PolyLine.Closed", () => polyline.Closed);
                return true;
            case Arc arc:
                Read(row, "center", arc, "Arc.Location", () => Point(arc.Location));
                Read(row, "radius", arc, "Arc.Radius", () => arc.Radius);
                Read(row, "startAngle", arc, "Arc.StartAngle", () => arc.StartAngle);
                Read(row, "endAngle", arc, "Arc.EndAngle", () => arc.EndAngle);
                Read(row, "kind", arc, "Arc.Kind", () => arc.Kind.ToString());
                Read(row, "firstRadius", arc, "Arc.FirstRadius", () => arc.FirstRadius);
                Read(row, "secondRadius", arc, "Arc.SecondRadius", () => arc.SecondRadius);
                Read(row, "ellipseAngle", arc, "Arc.ElipseAngle", () => arc.ElipseAngle);
                Read(row, "sector", arc, "Arc.Sector", () => arc.Sector);
                return true;
            case TextBase text:
                ReadText(text, row);
                return true;
            default:
                return false;
        }
    }

    private static void ReadRectangle(Rectangle rectangle, JObject row)
    {
        Read(row, "lowerLeft", rectangle, "Rectangle.LowerLeftCorner", () => Point(rectangle.LowerLeftCorner));
        Read(row, "upperRight", rectangle, "Rectangle.UpperRightCorner", () => Point(rectangle.UpperRightCorner));
        Read(row, "angle", rectangle, "Rectangle.Angle", () => rectangle.Angle);
    }

    private static void ReadText(TextBase text, JObject row)
    {
        Read(row, "content", text, "TextBase.Contents", () =>
        {
            using MultiLangString? contents = text switch
            {
                PropertyPlacement property => property.Contents,
                Text literal => literal.Contents,
                _ => text.Contents,
            };
            return contents is null ? null : ReadLanguages(contents, row);
        });
        Read(row, "height", text, "TextBase.Height", () => text.Height);
        // EPLAN documents TextBase.Rotation in radians.
        Read(row, "angle", text, "TextBase.Rotation", () => text.Rotation);
        Read(row, "alignment", text, "TextBase.Justification", () => text.Justification.ToString());
    }

    private static void ReadPropertyPlacement(PropertyPlacement property, JObject row)
    {
        object? parent = Get<object?>(row, property, "PropertyPlacement.Parent", () => property.Parent);
        if (parent is StorableObject parentObject)
        { Read(row, "parentOid", parentObject, "PropertyPlacement.Parent.ObjectIdentifier", () => parentObject.ObjectIdentifier); }
        StorableObject? source = Get<StorableObject?>(row, property, "PropertyPlacement.Source", () => property.Source);
        if (source is not null)
        { Read(row, "sourceOid", source, "PropertyPlacement.Source.ObjectIdentifier", () => source.ObjectIdentifier); }
        AnyPropertyId? displayed = Get<AnyPropertyId?>(row, property, "PropertyPlacement.DisplayedProperty", () => property.DisplayedProperty);
        if (displayed is not null)
        {
            try
            {
                Read(row, "propertyId", displayed, "AnyPropertyId.AsInt", () => displayed.AsInt);
                Read(row, "propertyIndex", displayed, "AnyPropertyId.Index", () => displayed.Index);
            }
            finally
            { displayed.Dispose(); GC.KeepAlive(property); }
        }
        Read(row, "isVisible", property, "GraphicalPlacement.IsVisible", () => property.IsVisible);
        GC.KeepAlive(parent);
        GC.KeepAlive(source);
        GC.KeepAlive(property);
    }
}
