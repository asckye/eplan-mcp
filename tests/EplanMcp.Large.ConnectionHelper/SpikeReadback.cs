using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.EObjects;
using EplanMcp.Bridge.Core.Operations;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Detached raw evidence; native counterpart membership and rendered text are deliberately separate fields.</summary>
internal static class SpikeReadback
{
    internal static JObject Read(Project project, string[] names)
    {
        Page[] pages = names.Select(n => project.Pages.Single(p => p.Name == n)).ToArray();
        SymbolReference[] references = pages.SelectMany(p => p.AllPlacements.OfType<SymbolReference>()).ToArray();
        var scene = new ConnectionCreatePlanner.Scene();
        var segments = new JArray();
        foreach (SymbolReference reference in references)
        {
            foreach (PinBase pin in reference.GraphicalConnectionPoints)
            { scene.Pins[FormattableString.Invariant($"symbol/{reference.ObjectIdentifier}/{pin.Index}")] = new() { Location = At(reference, pin) }; }
            foreach (var line in reference.GraphicalConnections)
            {
                var row = new JObject
                {
                    ["page"] = reference.Page.Name,
                    ["startOid"] = line.StartSymbolRef.ObjectIdentifier,
                    ["endOid"] = line.EndSymbolRef.ObjectIdentifier,
                    ["start"] = new JArray(At(line.StartSymbolRef, line.StartConnectionPoint)),
                    ["end"] = new JArray(At(line.EndSymbolRef, line.EndConnectionPoint)),
                };
                string Key(JToken s) => new JArray(s["page"]!, new JArray(new[] { s["start"]!.ToString(), s["end"]!.ToString() }.OrderBy(v => v, StringComparer.Ordinal))).ToString();
                if (!segments.Any(s => Key(s) == Key(row)))
                { segments.Add(row); }
            }
        }
        using var finder = new DMObjectsFinder(project);
        var connections = new JArray();
        foreach (Connection connection in finder.GetConnections(null))
        {
            // Include a cross-page connection by either endpoint, regardless of which page EPLAN puts Connection.Page on.
            if (connection is Connection3D || !(names.Contains(PageName(connection), StringComparer.Ordinal)
                || names.Contains(connection.StartSymbolReference?.Page?.Name, StringComparer.Ordinal)
                || names.Contains(connection.EndSymbolReference?.Page?.Name, StringComparer.Ordinal)))
            { continue; }
            JObject row = ConnectionRow(connection);
            Pin? start = connection.StartPin, end = connection.EndPin;
            if (start?.ParentFunction is { } a && end?.ParentFunction is { } b && a.Page.Name == b.Page.Name)
            {
                scene.Graphical = segments.Where(s => (string?)s["page"] == a.Page.Name).Select(s => new[] { s["start"]!.ToObject<double[]>()!, s["end"]!.ToObject<double[]>()! }).ToList();
                double[][]? path = CableDefinitionPlanner.DrawnPath(scene, At(a, start), At(b, end));
                row["drawnPath"] = path is null ? JValue.CreateNull() : JArray.FromObject(path);
            }
            connections.Add(row);
        }
        return new JObject
        {
            ["version"] = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(Project).Assembly.Location).FileVersion,
            ["pagesInProjectOrder"] = new JArray(project.Pages.Where(p => names.Contains(p.Name, StringComparer.Ordinal)).Select(p => p.Name)),
            ["devices"] = new JArray(references.OfType<Function>().Where(f => f is not Cable).Select(f => new JObject
            { ["page"] = f.Page.Name, ["oid"] = f.ObjectIdentifier, ["name"] = f.Name, ["position"] = new JArray(f.Location.X, f.Location.Y) })),
            ["segments"] = segments,
            ["connections"] = connections,
            ["cables"] = new JArray(references.OfType<Cable>().Select(c => new JObject
            {
                ["page"] = c.Page.Name,
                ["oid"] = c.ObjectIdentifier,
                ["name"] = c.Name,
                ["crossedSegments"] = Observe(() => JArray.FromObject(c.GraphicalConnections.Select(g => new
                { startOid = g.StartSymbolRef.ObjectIdentifier, endOid = g.EndSymbolRef.ObjectIdentifier, start = At(g.StartSymbolRef, g.StartConnectionPoint), end = At(g.EndSymbolRef, g.EndConnectionPoint) }))),
                ["memberOids"] = Observe(() => new JArray(c.CableConnections.Select(w => w.ObjectIdentifier))),
                ["templates"] = Observe(() => new JArray(c.FunctionTemplates.OfType<Connection>().Select(ConnectionRow))),
            })),
            ["interruptions"] = new JArray(references.OfType<InterruptionPoint>().Select(InterruptionRow)),
        };
    }

    private static string? PageName(Connection connection)
    {
        try
        { return connection.Page?.Name; }
        catch (BaseException) { return null; } // A11: an unplaced native connection can have no readable page.
    }

    private static double[] At(SymbolReference owner, PinBase pin) => [owner.Location.X + pin.Location.X, owner.Location.Y + pin.Location.Y];
    private static JObject Identity(SymbolReference reference) => new()
    { ["page"] = reference.Page?.Name, ["oid"] = reference.ObjectIdentifier, ["name"] = reference is InterruptionPoint point ? point.Name : reference is FunctionBase f ? f.Name : reference.SymbolVariant.SymbolName, ["position"] = new JArray(reference.Location.X, reference.Location.Y) };
    private static JObject? Endpoint(Pin? pin, SymbolReference? reference)
    {
        SymbolReference? owner = pin?.ParentFunction ?? reference;
        if (owner is null)
        { return null; }
        JObject row = Identity(owner);
        row["pinIndex"] = pin?.Index;
        row["pinName"] = pin?.Name;
        return row;
    }

    private static JObject ConnectionRow(Connection connection)
    {
        bool template = connection.IsTemplate;
        using ConnectionPropertyList properties = connection.Properties;
        var row = new JObject
        {
            ["oid"] = Observe(() => connection.ObjectIdentifier),
            ["template"] = template,
            ["properties"] = PropertiesOf(properties, [20005, 31011, 31004, 31002]),
        };
        if (!template)
        {
            row["page"] = Observe(() => connection.Page?.Name);
            row["from"] = Endpoint(connection.StartPin, connection.StartSymbolReference);
            row["to"] = Endpoint(connection.EndPin, connection.EndSymbolReference);
            row["definitionPoints"] = new JArray(connection.ConnectionDefPoints.Select(p => new JObject
            { ["oid"] = p.ObjectIdentifier, ["name"] = p.Name, ["position"] = new JArray(p.Location.X, p.Location.Y), ["cableOid"] = p.CableDefinitionLine?.ObjectIdentifier }));
        }
        return row;
    }

    private static JObject InterruptionRow(InterruptionPoint point)
    {
        JObject row = Identity(point);
        using InterruptionPointPropertyList properties = point.Properties;
        row["properties"] = PropertiesOf(properties, [24300, 24301, 24351, 24000, 24020, 24021, 24809]);
        row["direction"] = point.GraphicalConnectionPoints.Single().Direction.ToString();
        row["starSource"] = point.IsStarSource;
        // The public API exposes All (StorableObject), not a primary-pair getter. Keep this reference group separate
        // from the electrical endpoints; attached devices let the reader correlate each point with those endpoints.
        row["counterpartsAll"] = Observe(() => new JArray(point.CrossReferencedObjectsAll.Where(p => p.ObjectIdentifier != point.ObjectIdentifier)
            .Select(p => p is SymbolReference reference ? Identity(reference) : new JObject { ["oid"] = p.ObjectIdentifier, ["type"] = p.GetType().Name })));
        row["attachedDevices"] = new JArray(point.GraphicalConnections.Select(g => g.StartSymbolRef.ObjectIdentifier == point.ObjectIdentifier ? g.EndSymbolRef : g.StartSymbolRef)
            .OfType<Function>().Select(Identity));
        row["displayText"] = Observe(() => new JArray(point.PropertyPlacements.Select(p =>
        {
            using AnyPropertyId id = p.DisplayedProperty;
            return new JObject { ["propertyId"] = id.AsInt, ["text"] = Observe(() => p.GetDisplayString()), ["offset"] = new JArray(p.Location.X, p.Location.Y) };
        })));
        return row;
    }

    private static JObject PropertiesOf(UniversalPropertyList properties, int[] numbers)
    {
        var row = new JObject();
        foreach (int number in numbers)
        {
            string key = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row[key] = Observe(() =>
            {
                using AnyPropertyId id = number;
                using PropertyValue value = properties[id];
                return value.IsEmpty ? null : value.ToString();
            });
        }
        return row;
    }

    private static JToken Observe(Func<object?> read)
    {
        try
        {
            object? value = read();
            return value is null ? JValue.CreateNull() : value is JToken token ? token : JToken.FromObject(value);
        }
        catch (Exception ex)
        {
            // A spike must retain an unavailable field as evidence without losing the other native observations.
            return new JObject { ["readError"] = ex.Message, ["type"] = ex.GetType().Name };
        }
    }
}
