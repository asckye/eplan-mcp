using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

public sealed partial class PageExportAction
{
    private static void ReadSymbolConnections(SymbolReference symbol, JObject row)
    {
        var points = new JArray();
        row["connectionPoints"] = points;
        foreach (PinBase pin in Get(row, symbol, "SymbolReference.GraphicalConnectionPoints", () => symbol.GraphicalConnectionPoints, []))
        {
            var point = new JObject();
            points.Add(point);
            Read(point, "index", pin, "PinBase.Index", () => pin.Index);
            Read(point, "direction", pin, "PinBase.Direction", () => pin.Direction.ToString());
            Read(point, "location", pin, "PinBase.Location", () => Point(pin.Location));
            // DataModelu.xml: PinBase.Location is relative to the symbol's insertion point.
            Read(point, "position", pin, "SymbolReference.Location + PinBase.Location", () =>
                Point(new PointD(symbol.Location.X + pin.Location.X, symbol.Location.Y + pin.Location.Y)));
            GC.KeepAlive(symbol);
        }
    }

    private static void ReadInterruption(InterruptionPoint point, JObject row)
    {
        Read(row, "name", point, "InterruptionPoint.Name", () => point.Name);
        Read(row, "visibleName", point, "InterruptionPoint.VisibleName", () => point.VisibleName);
        Read(row, "isStarSource", point, "InterruptionPoint.IsStarSource", () => point.IsStarSource);
        // A55/live 2026-11-03: Properties is a cached list. Disposing it here made the later generic read use a freed native
        // object (0xc0000374), so it stays alive for this object's whole read.
#pragma warning disable CA2000
        InterruptionPointPropertyList? properties = Get<InterruptionPointPropertyList?>(row, point, "InterruptionPoint.Properties", () => point.Properties);
#pragma warning restore CA2000
        if (properties is not null)
        {
            // Keep the API's raw cross-reference text. It is not necessarily a page name.
            Read(row, "counterpartReference", point, "INTERRUPTIONPOINT_CROSSREFDESTINATION", () =>
            {
                using PropertyValue value = properties.INTERRUPTIONPOINT_CROSSREFDESTINATION;
                return value.IsEmpty ? null : value.ToString();
            });
            Read(row, "destination", point, "INTERRUPTIONPOINT_DESTINATION", () =>
            {
                using PropertyValue value = properties.INTERRUPTIONPOINT_DESTINATION;
                return value.IsEmpty ? null : value.ToString();
            });
        }

        var connected = new HashSet<int>();
        foreach (SymbolReference.GraphicalConnection connection in Get(row, point, "SymbolReference.GraphicalConnections", () => point.GraphicalConnections, []))
        {
            Get(row, point, "GraphicalConnection.ConnectionPoint", () =>
            {
                SymbolReference start = connection.StartSymbolRef;
                SymbolReference end = connection.EndSymbolRef;
                try
                {
                    if (start.ObjectIdentifier == point.ObjectIdentifier)
                    { connected.Add(connection.StartConnectionPoint.Index); }
                    if (end.ObjectIdentifier == point.ObjectIdentifier)
                    { connected.Add(connection.EndConnectionPoint.Index); }
                    return true;
                }
                finally { GC.KeepAlive(start); GC.KeepAlive(end); }
            });
        }
        JArray points = (JArray)row["connectionPoints"]!;
        // Same A83 selection as the connection reader: prefer the actually connected symbol pin.
        JObject? selected = points.OfType<JObject>().LastOrDefault(pin => pin["index"] is JValue index && connected.Contains(index.Value<int>()))
            ?? points.OfType<JObject>().LastOrDefault();
        row["connectedPointIndexes"] = new JArray(connected.OrderBy(index => index));
        row["connectionPointIndex"] = selected?["index"]?.DeepClone();
        row["direction"] = selected?["direction"]?.DeepClone();
        GC.KeepAlive(point);
    }

    private static void ReadLocationBox(LocationBox box, JObject row)
    {
        Read(row, "name", box, "LocationBox.Name", () => box.Name);
        Read(row, "visibleName", box, "LocationBox.VisibleName", () => box.VisibleName);
        Read(row, "logicalArea", box, "LocationBox.GetLogicalArea", () =>
        {
            RectangleD area = box.GetLogicalArea();
            return new JObject { ["lowerLeft"] = Point(area.LowerLeft), ["upperRight"] = Point(area.UpperRight) };
        });
#pragma warning disable CA2000 // Same as the interruption point list: not disposed during the read.
        FunctionBasePropertyList? parts = Get<FunctionBasePropertyList?>(row, box, "LocationBox.NameParts", () => box.NameParts);
#pragma warning restore CA2000
        if (parts is not null)
        {
            row["nameParts"] = ReadProperties(box, parts, row);
            Read(row, "structure", box, "NameParts.DESIGNATION_LOCATION", () =>
            {
                using PropertyValue value = parts.DESIGNATION_LOCATION;
                return value.IsEmpty ? null : "+" + value.ToString();
            });
        }
    }

    private static void ReadBlock(Block block, JObject row, JObject unhandled)
    {
        Read(row, "name", block, "Block.Name", () => block.Name);
        var members = new JArray();
        var memberOids = new JArray();
        row["members"] = members;
        row["memberOids"] = memberOids;
        // SubPlacements exposes block-relative coordinates. Reuse the full row reader, including graphics styles,
        // text languages, read errors and nested blocks; member IDs are retained only as diagnostic evidence.
        foreach (Placement member in Get(row, block, "Block.SubPlacements", () => block.SubPlacements, []))
        {
            AddPlacement(member, members, unhandled);
            object? oid = Get<object?>(row, member, "Block.SubPlacements.ObjectIdentifier", () => member.ObjectIdentifier);
            if (oid is not null)
            { memberOids.Add(JToken.FromObject(oid)); }
            GC.KeepAlive(block);
        }
    }
}
