using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    private static IEnumerable<T> UniqueRead<T>(ReadRecord record, string field, IEnumerable<T?> source) where T : StorableObject
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (T? item in source)
        {
            string path = field + "[" + Evidence.Number(index++) + "]";
            if (item is null) { continue; }
            string? key = record.Get<string?>(path + ".identifier", item.ToStringIdentifier);
            if (string.IsNullOrEmpty(key) || seen.Add(key!)) { yield return item; }
        }
    }
    private static long? ReadOid(ReadRecord record, string field, StorableObject? obj) => record.Get<long?>(field, () => obj?.ObjectIdentifier);
    private static long? ReadPageOid(ReadRecord record, string field, Placement? obj) => record.Get<long?>(field, () => obj?.Page?.ObjectIdentifier);
    private Page[] ReadOwnedPages(ReadRecord record, Page[]? pages)
    {
        var owned = new List<Page>();
        foreach (Page? page in pages ?? [])
        {
            if (page is null) { continue; }
            long? oid = ReadOid(record, "page.oid", page);
            string key = "page[" + (oid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null") + "]";
            int? counter = record.Get<int?>(key + ".counter", () => Counter(page));
            if (counter is not (>= 9700 and <= 9799)) { continue; }
            string? description = record.Get<string?>(key + ".description", () => Description(page));
            if (description?.StartsWith("S43 ", StringComparison.Ordinal) == true) { owned.Add(page); }
        }
        return owned.ToArray();
    }
    internal JObject Read()
    {
        var row = new ReadRecord();
        using var quiet = row.Get<QuietModeStep?>("quiet", () => new QuietModeStep(QuietModes.ShowNoDialogs));
        using var finder = row.Get<DMObjectsFinder?>("finder", () => new DMObjectsFinder(project));
        Page[]? pages = row.Get<Page[]?>("sourcePages", () => project.Pages);
        Page[] owned = ReadOwnedPages(row, pages);
        var ownedIds = new HashSet<long?>(owned.Select(p => ReadOid(row, "ownedPage.oid", p)).Where(id => id is not null));
        Function[]? functions = row.Get<Function[]?>("sourceFunctions", () => finder?.GetFunctions(null));
        Connection[]? connections = row.Get<Connection[]?>("sourceConnections", () => finder?.GetConnections(null));
        InterruptionPoint[]? allPoints = row.Get<InterruptionPoint[]?>("sourceInterruptionPoints", () => finder?.GetInterruptionPoints(null));
        InterruptionPoint[] points = UniqueRead(row, "interruptionPoints", (allPoints ?? []).Concat(_returned.OfType<InterruptionPoint>()))
            .Where(p => row.Get("interruption.nameMatch", () => IsS43Link(p)) && ownedIds.Contains(ReadPageOid(row, "interruption.page", p))).ToArray();
        var pointIds = new HashSet<long?>(points.Select(p => ReadOid(row, "interruption.oid", p)).Where(id => id is not null));
        row.Array("pageOids", () => pages?.OrderBy(p => ReadOid(row, "page.oid", p)), p => new JValue(ReadOid(row, "page.oid", p)));
        row.Array("functionOids", () => functions?.OrderBy(f => ReadOid(row, "function.oid", f)), f => new JValue(ReadOid(row, "function.oid", f)));
        row.Array("connectionOids", () => connections?.OrderBy(c => ReadOid(row, "connection.oid", c)), c => new JValue(ReadOid(row, "connection.oid", c)));
        row.Array("interruptionOids", () => allPoints?.OrderBy(p => ReadOid(row, "interruption.oid", p)), p => new JValue(ReadOid(row, "interruption.oid", p)));
        row.Array("interruptionPairing", () => points, point =>
        {
            var pair = new ReadRecord();
            long? oid = ReadOid(pair, "oid", point);
            StorableObject[]? peers = pair.Get<StorableObject[]?>("crossReferencedObjectsAll", () => point.CrossReferencedObjectsAll);
            pair.Field("oid", () => oid);
            pair.Field("page", () => point.Page?.Name);
            pair.Array("peerOids", () => peers, peer => new JValue(ReadOid(pair, "peer.oid", peer)));
            pair.Field("referencesOtherS43Point", () => peers is null ? null : peers.Any(peer =>
            {
                long? id = ReadOid(pair, "peer.oid", peer);
                return id is not null && id != oid && pointIds.Contains(id);
            }));
            return pair.Data;
        });
        row.Array("pages", () => pages is null ? null : owned, ReadPage);
        return row.Data;
    }
    private JObject ReadPage(Page page)
    {
        var row = new ReadRecord();
        long? pageId = ReadOid(row, "oid", page);
        Placement[]? placements = row.Get<Placement[]?>("allPlacements", () => page.AllPlacements);
        SymbolReference[] references = UniqueRead(row, "elements", (placements ?? []).OfType<SymbolReference>()
            .Concat(_returned.Where(r => pageId is not null && ReadPageOid(row, "returned.page", r) == pageId)))
            .OrderBy(r => ReadOid(row, "element.oid", r)).ToArray();
        using var finder = row.Get<DMObjectsFinder?>("finder", () => new DMObjectsFinder(project));
        Connection[]? found = row.Get<Connection[]?>("sourceConnections", () => finder?.GetConnections(null));
        var pinConnections = references.OfType<Function>().SelectMany(f => row.Get<Pin[]?>("function.pins", () => f.Pins) ?? [])
            .Where(p => p is not null).SelectMany(p => row.Get<Connection[]?>("pin.connections", () => p.Connections) ?? []);
        Connection[] connections = UniqueRead(row, "connections", (found ?? []).Where(c => c is not null && pageId is not null &&
            row.Get<long?>("connection.page", () => c.Page?.ObjectIdentifier) == pageId).Concat(pinConnections))
            .OrderBy(c => ReadOid(row, "connection.oid", c)).ToArray();
        row.Field("oid", () => pageId);
        row.Field("name", () => page.Name);
        row.Field("counter", () => Counter(page));
        row.Field("description", () => Description(page));
        row.Field("grid", () => page.GridSize);
        row.Array("placementOids", () => placements?.OrderBy(p => ReadOid(row, "placement.oid", p)), p => new JValue(ReadOid(row, "placement.oid", p)));
        row.Array("elements", () => placements is null && references.Length == 0 ? null : references, ReferenceData);
        row.Array("connections", () => connections, ConnectionData);
        return row.Data;
    }
    private JObject? ReferenceData(SymbolReference? reference)
    {
        if (reference is null) { return null; }
        var row = new ReadRecord();
        bool? placed = row.Get<bool?>("isPlaced", () => reference.IsPlaced);
        var variant = placed == true ? row.Get("symbolVariant", () => reference.SymbolVariant) : null;
        row.Field("oid", () => reference.ObjectIdentifier);
        row.Field("runtimeType", () => reference.GetType().FullName);
        row.Field("isPlaced", () => placed);
        row.Field("location", () => XY(reference.Location));
        row.Field("library", () => variant?.SymbolLibraryName);
        row.Field("symbol", () => variant?.SymbolName);
        row.Field("variant", () => variant?.VariantNr);
        row.Array("graphicalPins", () => reference.GraphicalConnectionPoints, p => PinData(p, reference));
        row.Array("graphicalConnections", () => reference.GraphicalConnections, c =>
        {
            var connection = new ReadRecord();
            connection.Field("start", () => PinData(c.StartConnectionPoint, c.StartSymbolRef));
            connection.Field("end", () => PinData(c.EndConnectionPoint, c.EndSymbolRef));
            return connection.Data;
        });
        if (reference is LocationBox or PLC or Cable)
        {
            row.Array("logicalArea", () => reference.LogicalAreaSegments, segment =>
            {
                var area = new ReadRecord();
                area.Field("start", () => XY(segment.StartPoint));
                area.Field("end", () => XY(segment.EndPoint));
                return area.Data;
            });
        }
        if (reference is FunctionBase named)
        {
            row.Field("name", () => named.Name);
            row.Field("visibleName", () => named.VisibleName);
            row.Field("locationStructure", () => Value(named, 1200));
            row.Field("plantStructure", () => Value(named, 1100));
            row.Array("display", () => named.PropertyPlacements?.OrderBy(p => ReadOid(row, "display.oid", p)), DisplayData);
        }
        if (reference is Function function)
        {
            FunctionDefinition? definition = row.Get<FunctionDefinition?>("functionDefinition", () => function.FunctionDefinition);
            Pin[]? pins = row.Get<Pin[]?>("sourcePins", () => function.Pins);
            row.Field("category", () => function.FunctionCategory.ToString());
            row.Field("definitionId", () => definition?.Id);
            row.Field("definitionGroup", () => definition?.Group);
            row.Field("definitionName", () => definition is null ? null : DefinitionText(definition, row));
            row.Field("main", () => function.IsMainFunction);
            row.Field("parts", () => function.ArticleReferences?.Length);
            row.Array("pins", () => pins, p => PinData(p, function));
            if (function is Terminal && row.Get("terminal.category", () => function.FunctionCategory) == Eplan.EplApi.Base.Enums.FunctionCategory.Terminal)
            { row.Field("designation", () => Value(function, 20030)); }
            if (pins is { Length: > 0 }) { row.Field("allConnectionDesignations20038", () => Value(function, PlcFields.ConnectionDesignations)); }
            else if (pins is null) { row.Data["allConnectionDesignations20038"] = null; }
            row.Array("connectionDesignations", () => pins?.Select((p, i) => (Pin: p, Index: i + 1)), item =>
            {
                if (item.Pin is null) { return null; }
                var designation = new ReadRecord();
                designation.Field("index", () => item.Index);
                designation.Field("input20022", () => Value(function, 20022, item.Index));
                designation.Field("computed20028", () => Value(function, 20028, item.Index));
                return designation.Data;
            });
        }
        if (reference is InterruptionPoint point)
        {
            row.Field("designationState", () => ObjectState(point));
            row.Field("crossReference24300", () => Value(point, 24300));
            row.Field("destination24000", () => Value(point, 24000));
            row.Field("description24810", () => Value(point, 24810, multilingual: true));
            row.Array("crossReferencedObjectsAll", () => point.CrossReferencedObjectsAll, obj =>
            {
                var peer = new ReadRecord();
                peer.Field("oid", () => obj.ObjectIdentifier);
                peer.Field("runtimeType", () => obj.GetType().FullName);
                return peer.Data;
            });
        }
        if (reference is LocationBox box) { row.Field("graphics", () => GraphicsData(box.GetGraphics())); }
        if (reference is TerminalStrip strip)
        {
            var data = new ReadRecord();
            data.Field("name", () => strip.Name);
            data.Array("terminals", () => strip.Terminals, t => TerminalData(t, false));
            row.Data["terminalStripGet"] = data.Data;
        }
        if (reference is Terminal terminal)
        { row.Field("terminal", () => TerminalData(terminal, terminal.FunctionCategory == Eplan.EplApi.Base.Enums.FunctionCategory.PLCTerminal)); }
        if (reference is PLC plc)
        {
            var data = new ReadRecord();
            data.Field("tag", () => plc.Name);
            data.Field("visibleTag", () => plc.VisibleName);
            data.Field("page", () => plc.Page?.Name);
            data.Field("cpu", () => plc.IsCPU);
            data.Field("station", () => Value(plc, 20408));
            data.Field("rack", () => Value(plc, 20410));
            data.Field("slot", () => Value(plc, 20411));
            data.Array("channels", () => plc.PLCTerminals, t => TerminalData(t, true));
            row.Data["plcGet"] = data.Data;
        }
        if (reference is Cable cable)
        {
            row.Field("cableType", () => Value(cable, 20040));
            row.Field("coreCount", () => Value(cable, 20042));
            row.Field("crossSection", () => Value(cable, 20043));
            row.Array("cableConnections", () => cable.CableConnections?.OrderBy(c => ReadOid(row, "cableConnection.oid", c)), ConnectionData);
            row.Array("templates", () => cable.FunctionTemplates?.OfType<Connection>(), ConnectionData);
        }
        return row.Data;
    }
    private JObject? DisplayData(PropertyPlacement? placement)
    {
        if (placement is null) { return null; }
        var row = new ReadRecord();
        using AnyPropertyId? id = row.Get<AnyPropertyId?>("displayedProperty", () => placement.DisplayedProperty);
        row.Field("oid", () => placement.ObjectIdentifier);
        row.Field("propertyId", () => id?.AsInt);
        row.Field("index", () => id?.Index);
        row.Field("text", placement.GetDisplayString);
        row.Field("location", () => XY(placement.Location));
        row.Field("visible", () => placement.IsVisible);
        return row.Data;
    }
    private JObject? TerminalData(Terminal? terminal, bool plc)
    {
        if (terminal is null) { return null; }
        var row = new ReadRecord();
        Pin[]? pins = row.Get<Pin[]?>("sourcePins", () => terminal.Pins);
        row.Field("oid", () => terminal.ObjectIdentifier);
        row.Field("name", () => terminal.Name);
        row.Field("page", () => terminal.Page?.Name);
        row.Field("main", () => terminal.IsMainFunction);
        row.Array("pins", () => pins, p => PinData(p, terminal));
        row.Array("connected", () => pins is null ? null : UniqueRead(row, "connected", pins.Where(p => p is not null)
            .SelectMany(p => row.Get<Connection[]?>("pin.connections", () => p.Connections) ?? [])), ConnectionData);
        if (plc)
        {
            row.Field("plc", () => terminal.PLC?.Name);
            row.Field("address", () => Value(terminal, PlcFields.Address));
            row.Field("symbolicAddress", () => Value(terminal, PlcFields.SymbolicCalculated));
            row.Field("manualSymbolicAddress", () => Value(terminal, PlcFields.SymbolicManual));
            row.Field("plug", () => Value(terminal, PlcFields.Plug));
            row.Field("channel", () => Value(terminal, PlcFields.Channel));
            row.Field("dataType", () => Value(terminal, PlcFields.DataType));
            row.Field("text", () => Value(terminal, PlcFields.Text, multilingual: true));
            row.Field("textLanguage", () => "en_US");
        }
        else
        {
            row.Field("designation", () => Value(terminal, 20030));
            row.Field("strip", () => terminal.TerminalStrip?.Name);
            row.Field("mainTerminal", () => terminal.IsMainTerminal);
            row.Array("inside", () => terminal.InternalConnections, c => ConnectionData(c.Conn));
            row.Array("outside", () => terminal.ExternalConnections, c => ConnectionData(c.Conn));
        }
        return row.Data;
    }
    private JObject? ConnectionData(Connection? connection)
    {
        if (connection is null) { return null; }
        var row = new ReadRecord();
        bool? template = row.Get<bool?>("template", () => connection.IsTemplate);
        Pin? start = template == false ? row.Get<Pin?>("startPin", () => connection.StartPin) : null;
        Pin? end = template == false ? row.Get<Pin?>("endPin", () => connection.EndPin) : null;
        row.Field("oid", () => connection.ObjectIdentifier);
        row.Field("template", () => template);
        row.Field("page", () => template == false ? connection.Page?.Name : null);
        row.Field("pageOid", () => template == false ? connection.Page?.ObjectIdentifier : null);
        row.Field("isPlaced", () => connection.IsPlaced);
        row.Field("start", () => PinData(start, start?.ParentFunction));
        row.Field("end", () => PinData(end, end?.ParentFunction));
        row.Field("wireColorOrNumber31004", () => Value(connection, 31004));
        row.Field("graphicalColor31005", () => Value(connection, 31005));
        row.Field("designation31011", () => Value(connection, 31011));
        row.Field("crossSection31002", () => Value(connection, 31002));
        if (template == false)
        {
            ConnectionDefinitionPoint[]? definitions = row.Get<ConnectionDefinitionPoint[]?>("connectionDefinitionPoints", () => connection.ConnectionDefPoints);
            row.Array("connectionDefinitionOids", () => definitions, p => new JValue(ReadOid(row, "definition.oid", p)));
            row.Array("connectionDefinitionNames", () => definitions, ObjectState);
            row.Array("connectionDefinitionCableOids", () => definitions, p =>
                new JValue(row.Get<long?>("definition.cableOid", () => p.CableDefinitionLine?.ObjectIdentifier)));
            Cable? cable = definitions is { Length: > 0 } ? row.Get<Cable?>("cableDefinitionLine", () => connection.CableDefinitionLine) : null;
            row.Field("cableDefinitionOid", () => cable?.ObjectIdentifier);
            row.Field("hasCableDefinitionLine", () => definitions is null || row.ErrorFor("cableDefinitionLine") is not null ? null : cable is not null);
            if (row.ErrorFor("cableDefinitionLine") is JToken error) { row.Data["cableDefinitionError"] = error; }
            UniversalPropertyList? parts = row.Get<UniversalPropertyList?>("cableNameParts", () =>
            {
                string? owner = connection.ToStringIdentifier();
                if (string.IsNullOrEmpty(owner)) { return null; }
                string key = "cable-name:" + owner;
                if (_lists.TryGetValue(key, out UniversalPropertyList? cached)) { return cached; }
                FunctionBasePropertyList? found = connection.CableNameParts;
                if (found is not null) { _lists.Add(key, owner, found); }
                return found;
            });
            if (parts is null) { row.Data["cableNamePartsFirstRead"] = null; }
            else
            {
                var name = new ReadRecord();
                foreach (int property in new[] { 1100, 1200, 20005 })
                {
                    name.Field(Evidence.Number(property), () =>
                    {
                        using AnyPropertyId id = property;
                        using PropertyValue? value = parts[id];
                        return value is null || value.IsEmpty ? null : value.ToString();
                    });
                }
                row.Data["cableNamePartsFirstRead"] = name.Data;
            }
            var properties = new ReadRecord();
            foreach (int id in new[] { 1100, 1200, 20005, 20008, 20215 }) { properties.Field(Evidence.Number(id), () => Value(connection, id)); }
            row.Data["cableIdentifyingProperties"] = properties.Data;
        }
        return row.Data;
    }
    private static JObject? PinData(PinBase? pin, SymbolReference? owner)
    {
        if (pin is null) { return null; }
        var row = new ReadRecord();
        PointD? location = row.Get<PointD?>("location", () => pin.Location);
        PointD? parent = owner is null ? null : row.Get<PointD?>("parentLocation", () => owner.Location);
        row.Field("index", () => pin.Index);
        row.Field("location", () => location is PointD p ? XY(p) : null);
        row.Field("direction", () => pin.Direction.ToString());
        row.Field("designation", () => (pin as Pin)?.Designation);
        row.Field("name", () => (pin as Pin)?.Name);
        row.Field("parentOid", () => owner?.ObjectIdentifier);
        row.Field("device", () => (owner as FunctionBase)?.Name);
        row.Field("pagePositionByAddition", () => location is PointD p && parent is PointD o ? XY(new PointD(o.X + p.X, o.Y + p.Y)) : null);
        return row.Data;
    }
    private static IEnumerable<GraphicalPlacement> Graphics(Placement? root)
    {
        if (root is GraphicalPlacement graphic) { yield return graphic; }
        if (root is Group group) { foreach (Placement child in group.SubPlacements) { foreach (GraphicalPlacement item in Graphics(child)) { yield return item; } } }
    }
    private static IEnumerable<GraphicalPlacement> ReadGraphics(ReadRecord row, Placement? root)
    {
        if (root is GraphicalPlacement graphic) { yield return graphic; }
        if (root is Group group)
        {
            Placement[]? children = row.Get<Placement[]?>("graphics.subPlacements", () => group.SubPlacements);
            foreach (Placement? child in children ?? []) { foreach (GraphicalPlacement item in ReadGraphics(row, child)) { yield return item; } }
        }
    }
    private static JObject? GraphicsData(Placement? root)
    {
        if (root is null) { return null; }
        var row = new ReadRecord();
        row.Field("oid", () => root.ObjectIdentifier);
        row.Field("runtimeType", () => root.GetType().FullName);
        row.Array("bounds", root.GetBoundingBox, XY);
        row.Array("pens", () => ReadGraphics(row, root), graphic =>
        {
            var penData = new ReadRecord();
            Pen? pen = penData.Get<Pen?>("pen", () => graphic.Pen);
            penData.Field("oid", () => graphic.ObjectIdentifier);
            penData.Field("styleId", () => pen?.StyleId);
            penData.Field("colorId", () => pen?.ColorId);
            penData.Field("width", () => pen?.Width);
            return penData.Data;
        });
        return row.Data;
    }
}
