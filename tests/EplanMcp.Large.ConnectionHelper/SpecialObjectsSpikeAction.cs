using System.Globalization;
using System.Xml.Linq;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Records SPECIAL creation and property propagation on one explicit scratch page.</summary>
public sealed class SpecialObjectsSpikeAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeSpecialObjectsSpike"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            using var locking = new LockingStep();
            MainThread.Use(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            JObject input = JObject.Parse(File.ReadAllText(file));
            Project project = new SelectionSet().GetCurrentProject(false);
            string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
            string page = (string?)input["page"] ?? "";
            if (!string.Equals(project.ProjectFullName, Path.Combine(home, "scratch", "writes", "LargeWrite"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase)
                || !page.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal))
            { throw new InvalidOperationException("Only an explicit new Circuit page of current scratch LargeWrite is permitted."); }
            _ = FindPage(project, page);
            result["page"] = page;
            result["mode"] = input["mode"]?.DeepClone();
            if ((string?)input["mode"] == "read")
            { result["observation"] = ReadPage(project, page); }
            else if ((string?)input["mode"] == "probe")
            { Probe(project, page, input, result); }
            else
            { throw new ArgumentException("Mode must be read or probe."); }
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static void Probe(Project project, string page, JObject input, JObject result)
    {
        result["xmlEvidence"] = ReadXmlEvidence();
        var steps = new JArray();
        var conclusions = new JArray();
        result["steps"] = steps;
        result["conclusions"] = conclusions;
        bool connectionReady = (bool?)input["connectionReady"] == true
            && input["x"]?.Type is JTokenType.Float or JTokenType.Integer
            && input["y"]?.Type is JTokenType.Float or JTokenType.Integer;
        double x = connectionReady ? (double)input["x"]! : 0;
        double y = connectionReady ? (double)input["y"]! : 0;
        string blackBoxTag = (string?)input["blackBoxTag"] ?? "";
        string busbarTag = (string?)input["busbarTag"] ?? "";
        if (blackBoxTag.Length == 0 || busbarTag.Length == 0)
        { throw new ArgumentException("Explicit isolated blackBoxTag and busbarTag are required."); }

        long? cdp = null;
        if (connectionReady)
        {
            cdp = Create(project, page, "CDP", x, y, null, false, steps);
            if (cdp is { } id)
            {
                JObject created = ObservePair(project, page, id);
                steps.Add(new JObject { ["name"] = "CDP after creation", ["observation"] = created });
                GenerateConnections(project, page, "CDP creation", steps);
                JObject generated = ObservePair(project, page, id);
                steps.Add(new JObject { ["name"] = "CDP after generation", ["observation"] = generated });
                JObject write = Write(project, "CDP color and cross-section", () =>
                {
                    Placement placement = FindPlacement(project, page, id);
                    try
                    {
                        using UniversalPropertyList properties = PropertiesOf(placement);
                        using AnyPropertyId color = (int)Properties.ConnectionDefinitionPoint.CONNECTION_WIRENUMBER;
                        using AnyPropertyId section = (int)Properties.ConnectionDefinitionPoint.CONNECTION_WIRECROSSSECTION;
                        properties[color] = "RD";
                        properties[section] = "1.5";
                        return id;
                    }
                    finally { GC.KeepAlive(placement); }
                });
                steps.Add(write);
                JObject beforeGeneration = ObservePair(project, page, id);
                write["beforeGeneration"] = beforeGeneration;
                GenerateConnections(project, page, "CDP property propagation", steps);
                JObject afterGeneration = ObservePair(project, page, id);
                write["afterGeneration"] = afterGeneration;
                write["connectionColorWritten"] = ConnectionPropertyEquals(afterGeneration, id, 31004, "RD");
                write["connectionCrossSectionWritten"] = ConnectionPropertyEquals(afterGeneration, id, 31002, "1.5");
                conclusions.Add("CDP: type=" + TypeOf(afterGeneration["object"]) + ", on connection=" + YesNo(HasDefinition(afterGeneration, id, "connectionDefPointIds"))
                    + ", color written=" + YesNo(PropertyEquals(afterGeneration["object"], 31004, "RD"))
                    + ", cross-section written=" + YesNo(PropertyEquals(afterGeneration["object"], 31002, "1.5"))
                    + ", connection color propagated=" + YesNo(ConnectionPropertyEquals(afterGeneration, id, 31004, "RD"))
                    + ", connection cross-section propagated=" + YesNo(ConnectionPropertyEquals(afterGeneration, id, 31002, "1.5")));
            }
            else
            { conclusions.Add("CDP: creation failed; see full exception."); }
        }
        else
        {
            steps.Add(new JObject { ["name"] = "CDP", ["warning"] = "No confirmed tool-created connection midpoint; on-line creation was not attempted." });
            conclusions.Add("CDP: not attempted; no confirmed connection midpoint.");
        }

        long? cdpng = Create(project, page, "CDPNG", 150, 60, null, false, steps);
        if (cdpng is { } cdpngId)
        {
            JObject observation = ReadObject(project, page, cdpngId);
            steps.Add(new JObject { ["name"] = "CDPNG readback", ["observation"] = observation });
            conclusions.Add("CDPNG: type=" + TypeOf(observation) + ", created off-line=yes");
        }
        else
        { conclusions.Add("CDPNG: creation failed; see full exception."); }

        if (connectionReady)
        {
            long? pdp = Create(project, page, "PDP", x, y, null, false, steps);
            if (pdp is { } id)
            {
                steps.Add(new JObject { ["name"] = "PDP after creation", ["observation"] = ObservePair(project, page, id) });
                JObject write = Write(project, "PDP potential name", () =>
                {
                    Placement placement = FindPlacement(project, page, id);
                    try
                    {
                        using UniversalPropertyList properties = PropertiesOf(placement);
                        using AnyPropertyId potentialName = (int)Properties.PotentialDefinition.POTENTIAL_NAME;
                        properties[potentialName] = "G4G8_POTENTIAL";
                        return id;
                    }
                    finally { GC.KeepAlive(placement); }
                });
                steps.Add(write);
                write["beforeGeneration"] = ObservePair(project, page, id);
                GenerateConnections(project, page, "PDP property propagation", steps);
                JObject observation = ObservePair(project, page, id);
                write["afterGeneration"] = observation;
                conclusions.Add("PDP: type=" + TypeOf(observation["object"]) + ", potential name written="
                    + YesNo(PropertyEquals(observation["object"], 33000, "G4G8_POTENTIAL")));
            }
            else
            { conclusions.Add("PDP: creation failed; see full exception."); }
        }
        else
        {
            steps.Add(new JObject { ["name"] = "PDP", ["warning"] = "No confirmed tool-created connection midpoint; on-line creation was not attempted." });
            conclusions.Add("PDP: not attempted; no confirmed connection midpoint.");
        }

        long? box = Create(project, page, "DC", 220, 140, blackBoxTag, true, steps);
        if (box is { } boxId)
        {
            steps.Add(new JObject { ["name"] = "DC after creation", ["observation"] = ReadObject(project, page, boxId) });
            JObject resize = Write(project, "DC set logical area", () =>
            {
                Placement placement = FindPlacement(project, page, boxId);
                try
                {
                    if (placement is not SymbolReference symbol)
                    { throw new InvalidOperationException("Created DC is not a SymbolReference."); }
                    symbol.SetLogicalArea(new PointD(220, 100), new PointD(280, 140));
                    return boxId;
                }
                finally { GC.KeepAlive(placement); }
            });
            steps.Add(resize);
            resize["observation"] = ReadObject(project, page, boxId);
        }
        long? dcp = Create(project, page, "DCP", 240, 120, blackBoxTag + ":1", false, steps);
        JObject boxGroup = ReadGroup(project, page, box, dcp);
        steps.Add(new JObject { ["name"] = "DC and DCP relationship", ["observation"] = boxGroup });
        conclusions.Add("DC/DCP: same device=" + YesNo(ReadBoolean(boxGroup["sameIdentifyingDeviceTag"]))
            + ", DCP inside box=" + YesNo(ReadBoolean(boxGroup["secondInsideFirst"])));

        long? bbdef = Create(project, page, "BBDEF", 320, 140, busbarTag, false, steps);
        if (bbdef is { } bbdefId)
        { steps.Add(new JObject { ["name"] = "BBDEF readback", ["observation"] = ReadObject(project, page, bbdefId) }); }
        long? bcp = Create(project, page, "BCP", 320, 100, busbarTag, false, steps);
        JObject busbarGroup = ReadGroup(project, page, bbdef, bcp);
        steps.Add(new JObject { ["name"] = "BBDEF and BCP relationship", ["observation"] = busbarGroup });
        conclusions.Add("BBDEF/BCP: same device=" + YesNo(ReadBoolean(busbarGroup["sameIdentifyingDeviceTag"])));
        result["finalObservation"] = ReadPage(project, page);
    }

    private static long? Create(Project project, string page, string symbol, double x, double y, string? name, bool boxed, JArray steps)
    {
        var row = new JObject
        {
            ["name"] = symbol + " create",
            ["symbol"] = new JObject { ["library"] = "SPECIAL", ["name"] = symbol, ["variant"] = 0 },
            ["location"] = new JArray(x, y),
            ["requestedName"] = name,
            ["creationMethod"] = boxed ? "BoxedDevice.Create(Page); SymbolVariant = SPECIAL/DC/0" : "Function.Create(Page, SymbolVariant)",
        };
        steps.Add(row);
        try
        {
            // A52: discovery must finish before the write's locking/transaction scope.
            SymbolVariant variant = project.SymbolLibraries.Single(library => library.Name == "SPECIAL").Symbols
                .Single(candidate => candidate.Name == symbol).Variants.Single(candidate => candidate.VariantNr == 0);
            JObject write = Write(project, symbol + " create", () =>
            {
                Page target = FindPage(project, page);
#pragma warning disable CA2000 // A39/A55: newly created native storable objects are borrowed, including on Abort.
                Function function;
                if (boxed)
                {
                    var box = new BoxedDevice();
                    box.Create(target);
                    box.SymbolVariant = variant;
                    function = box;
                }
                else
                {
                    function = new Function();
                    function.Create(target, variant);
                }
#pragma warning restore CA2000
                function.Location = new PointD(x, y);
                if (name is not null)
                { function.Name = name; }
                long id = function.ObjectIdentifier;
                GC.KeepAlive(function);
                return id;
            });
            row.Merge(write);
            return (bool?)write["committed"] == true ? (long?)write["oid"] : null;
        }
        catch (Exception error) { row["error"] = error.ToString(); return null; }
    }

    private static JObject Write(Project project, string name, Func<long> mutation)
    {
        var row = new JObject { ["name"] = name, ["committed"] = false };
        try
        {
            // A13/A52/S41: Function.Create requires Transaction, not SafetyPoint.
            project.LockAllObjects();
            using var manager = new UndoManager();
            using UndoStep undo = manager.CreateUndoStep();
            undo.SetUndoDescription("SPECIAL spike: " + name);
            using Transaction transaction = new TransactionManager().CreateTransaction();
            try
            {
                long oid;
                using (var quiet = new QuietModeStep(QuietModes.ShowNoDialogs))
                { oid = mutation(); }
                row["commitAttempted"] = true;
                transaction.Commit();
                row["oid"] = oid;
                row["committed"] = true;
                try
                { undo.CloseUndo(); }
                catch (Exception closeError) { row["closeUndoError"] = closeError.ToString(); }
            }
            catch (Exception error)
            {
                row["error"] = error.ToString();
                try
                { transaction.Abort(); row["rolledBack"] = true; }
                catch (Exception rollbackError) { row["rollbackError"] = rollbackError.ToString(); }
            }
        }
        catch (Exception error) { row["error"] = error.ToString(); }
        return row;
    }

    private static void GenerateConnections(Project project, string page, string reason, JArray steps)
    {
        var row = new JObject { ["name"] = reason + " Generate.Connections", ["committed"] = false };
        steps.Add(row);
        try
        {
            using var scope = new TransactionScope("SPECIAL spike: " + reason);
            try
            {
                using (var generate = new Generate())
                { generate.Connections([FindPage(project, page)], false); }
                row["commitAttempted"] = true;
                scope.Commit();
                row["committed"] = true;
                try
                { scope.CloseUndo(); }
                catch (Exception closeError) { row["closeUndoError"] = closeError.ToString(); }
            }
            catch (Exception error)
            {
                row["error"] = error.ToString();
                try
                { scope.Rollback(); row["rolledBack"] = true; }
                catch (Exception rollbackError) { row["rollbackError"] = rollbackError.ToString(); }
            }
        }
        catch (Exception error) { row["error"] = error.ToString(); }
    }

    private static Page FindPage(Project project, string page) => project.Pages.Single(candidate => candidate.Name == page && candidate.PageType.ToString() == "Circuit");
    private static Placement FindPlacement(Project project, string page, long id) => FindPage(project, page).AllPlacements.Single(placement => placement.ObjectIdentifier == id && placement is not PropertyPlacement);

    private static JObject ReadPage(Project project, string page) => new()
    {
        ["placements"] = Observe(() => new JArray(FindPage(project, page).AllPlacements.Where(placement => placement is not PropertyPlacement).Select(ReadPlacement))),
        ["connections"] = ReadConnections(project, page),
    };

    private static JObject ReadObject(Project project, string page, long id)
    {
        try
        { return ReadPlacement(FindPlacement(project, page, id)); }
        catch (Exception error) { return new JObject { ["oid"] = id, ["error"] = error.ToString() }; }
    }

    private static JObject ObservePair(Project project, string page, long id) => new()
    { ["object"] = ReadObject(project, page, id), ["connections"] = ReadConnections(project, page) };

    private static JObject ReadPlacement(Placement placement)
    {
        var row = new JObject { ["type"] = placement.GetType().FullName, ["category"] = null };
        try
        {
            row["oid"] = Observe(() => placement.ObjectIdentifier);
            row["page"] = Observe(() => placement.Page?.Name);
            row["location"] = Observe(() => Point(placement.Location));
            row["boundingBox"] = Observe(() => new JArray(placement.GetBoundingBox().Select(Point)));
            if (placement is SymbolReference symbol)
            {
                row["symbol"] = Observe(() => symbol.SymbolVariant is { } variant ? new JObject
                { ["library"] = variant.SymbolLibraryName, ["name"] = variant.SymbolName, ["variant"] = variant.VariantNr } : null);
                row["logicalArea"] = Observe(() =>
                {
                    RectangleD area = symbol.GetLogicalArea();
                    return new JObject
                    {
                        ["lowerLeft"] = Point(area.LowerLeft),
                        ["upperRight"] = Point(area.UpperRight),
                        ["width"] = area.Width,
                        ["height"] = area.Height,
                    };
                });
            }
            if (placement is Function function)
            {
                row["name"] = Observe(() => function.Name);
                row["category"] = Observe(() => function.FunctionCategory.ToString());
                row["isMainFunction"] = Observe(() => function.IsMainFunction);
                row["graphics"] = Observe(() => function.GetGraphics() is Rectangle rectangle ? new JObject
                {
                    ["type"] = rectangle.GetType().FullName,
                    ["width"] = rectangle.Width,
                    ["height"] = rectangle.Height,
                    ["lowerLeft"] = Point(rectangle.LowerLeftCorner),
                    ["upperRight"] = Point(rectangle.UpperRightCorner),
                } : null);
            }
            if (placement is PotentialDefinition potential)
            { row["potentialName"] = Observe(() => potential.PotentialName); }
            if (placement is BoxedDevice box)
            { row["functionsInside"] = Observe(() => new JArray(box.FunctionsInside.Select(function => function.ObjectIdentifier))); }
            // A80: one cached property list per wrapper; all values released before keeping the owner alive.
            using UniversalPropertyList properties = PropertiesOf(placement);
            row["properties"] = ReadProperties(placement, properties);
        }
        catch (Exception error) { row["error"] = error.ToString(); }
        finally { GC.KeepAlive(placement); }
        return row;
    }

    private static JToken ReadConnections(Project project, string page)
    {
        return Observe(() =>
        {
            using var finder = new DMObjectsFinder(project);
            var rows = new JArray();
            foreach (Connection connection in finder.GetConnections(null))
            {
                try
                {
                    if (connection.Page?.Name != page)
                    { continue; }
                    var row = new JObject
                    {
                        ["oid"] = Observe(() => connection.ObjectIdentifier),
                        ["type"] = connection.GetType().FullName,
                        ["start"] = Observe(() => Endpoint(connection.StartPin)),
                        ["end"] = Observe(() => Endpoint(connection.EndPin)),
                        ["connectionDefPointIds"] = Observe(() => new JArray(connection.ConnectionDefPoints.Select(point => point.ObjectIdentifier))),
                        ["potentialDefinitionIds"] = Observe(() => new JArray(connection.PotentialDefinitions.Select(point => point.ObjectIdentifier))),
                    };
                    using UniversalPropertyList properties = connection.Properties;
                    row["properties"] = ReadProperties(connection, properties);
                    rows.Add(row);
                }
                catch (Exception error) { rows.Add(new JObject { ["error"] = error.ToString() }); }
                finally { GC.KeepAlive(connection); }
            }
            return rows;
        });
    }

    private static JObject? Endpoint(Pin? pin)
    {
        if (pin is null)
        { return null; }
        Function owner = pin.ParentFunction;
        try
        { return new JObject { ["oid"] = owner.ObjectIdentifier, ["name"] = owner.Name, ["pinIndex"] = pin.Index, ["pinName"] = pin.Name }; }
        finally { GC.KeepAlive(owner); GC.KeepAlive(pin); }
    }

    private static JObject ReadProperties(StorableObject owner, UniversalPropertyList properties)
    {
        var values = new JObject();
        var errors = new JArray();
        AnyPropertyId[] ids = [];
        try
        {
            ids = properties.ExistingIds;
            foreach (AnyPropertyId id in ids)
            {
                string key = id.AsInt.ToString(CultureInfo.InvariantCulture);
                try
                {
                    using PropertyValue value = properties[id];
                    using PropertyDefinition definition = value.Definition;
                    if (definition.IsIndexed)
                    {
                        var indexes = new JObject();
                        foreach (int index in value.Indexes)
                        {
                            try
                            {
                                using PropertyValue indexed = value[index];
                                if (!indexed.IsEmpty && indexed.ToString() is { Length: > 0 } text)
                                { indexes[index.ToString(CultureInfo.InvariantCulture)] = text; }
                            }
                            catch (Exception error) { errors.Add(new JObject { ["id"] = key, ["index"] = index, ["error"] = error.ToString() }); }
                        }
                        if (indexes.HasValues)
                        { values[key] = indexes; }
                    }
                    else if (!value.IsEmpty && value.ToString() is { Length: > 0 } text)
                    { values[key] = text; }
                }
                catch (Exception error) { errors.Add(new JObject { ["id"] = key, ["error"] = error.ToString() }); }
            }
        }
        catch (Exception error) { errors.Add(new JObject { ["error"] = error.ToString() }); }
        finally
        {
            foreach (AnyPropertyId id in ids)
            { id.Dispose(); }
            GC.KeepAlive(owner);
        }
        return new JObject { ["values"] = values, ["errors"] = errors };
    }

    private static PlacementPropertyList PropertiesOf(Placement placement) => placement switch
    {
        ConnectionDefinitionPoint point => point.Properties,
        PotentialDefinition potential => potential.Properties,
        Function function => function.Properties,
        SymbolReference symbol => symbol.Properties,
        _ => placement.Properties,
    };

    private static JObject ReadGroup(Project project, string page, long? first, long? second)
    {
        JObject? a = first is { } firstId ? ReadObject(project, page, firstId) : null;
        JObject? b = second is { } secondId ? ReadObject(project, page, secondId) : null;
        string? firstTag = (string?)a?["properties"]?["values"]?["20005"];
        string? secondTag = (string?)b?["properties"]?["values"]?["20005"];
        var row = new JObject
        {
            ["first"] = a,
            ["second"] = b,
            ["sameIdentifyingDeviceTag"] = firstTag is { Length: > 0 } && secondTag is { Length: > 0 }
                ? new JValue(string.Equals(firstTag, secondTag, StringComparison.Ordinal)) : JValue.CreateNull(),
        };
        if (first is { } boxId && second is { } pointId)
        {
            row["secondInsideFirst"] = Observe(() =>
            {
                Placement box = FindPlacement(project, page, boxId);
                Placement point = FindPlacement(project, page, pointId);
                try
                { return box is BoxedDevice device && point is Function function ? (bool?)device.IsFunctionInside(function) : null; }
                finally { GC.KeepAlive(box); GC.KeepAlive(point); }
            });
        }
        return row;
    }

    private static JObject ReadXmlEvidence()
    {
        string file = Path.ChangeExtension(typeof(Project).Assembly.Location, ".xml");
        var result = new JObject { ["path"] = file };
        try
        {
            XElement[] members = XDocument.Load(file).Descendants("member").ToArray();
            string[] names =
            [
                "T:Eplan.EplApi.DataModel.ConnectionDefinitionPoint", "M:Eplan.EplApi.DataModel.ConnectionDefinitionPoint.#ctor",
                "T:Eplan.EplApi.DataModel.PotentialDefinition", "M:Eplan.EplApi.DataModel.PotentialDefinition.#ctor",
                "T:Eplan.EplApi.DataModel.BoxedDevice", "M:Eplan.EplApi.DataModel.BoxedDevice.Create(Eplan.EplApi.DataModel.Page)",
                "M:Eplan.EplApi.DataModel.Function.Create(Eplan.EplApi.DataModel.Page,Eplan.EplApi.DataModel.MasterData.SymbolVariant)",
                "P:Eplan.EplApi.DataModel.SymbolReference.SymbolVariant",
                "M:Eplan.EplApi.DataModel.SymbolReference.SetLogicalArea(Eplan.EplApi.Base.PointD,Eplan.EplApi.Base.PointD)",
                "M:Eplan.EplApi.DataModel.SymbolReference.GetLogicalArea", "M:Eplan.EplApi.DataModel.Function.GetGraphics",
                "P:Eplan.EplApi.DataModel.FunctionPropertyList.RECTANGLE_WIDTH", "P:Eplan.EplApi.DataModel.FunctionPropertyList.RECTANGLE_HEIGHT",
                "F:Eplan.EplApi.DataModel.Properties.ConnectionDefinitionPoint.CONNECTION_WIRENUMBER",
                "F:Eplan.EplApi.DataModel.Properties.ConnectionDefinitionPoint.CONNECTION_WIRECROSSSECTION",
                "F:Eplan.EplApi.DataModel.Properties.PotentialDefinition.POTENTIAL_NAME",
                "P:Eplan.EplApi.DataModel.Connection.ConnectionDefPoints", "P:Eplan.EplApi.DataModel.Connection.PotentialDefinitions",
            ];
            result["members"] = new JArray(names.Select(name => new JObject
            {
                ["signature"] = name,
                ["xml"] = members.FirstOrDefault(member => (string?)member.Attribute("name") == name)?.ToString(),
            }));
            result["dedicatedCreateEntries"] = new JArray(members.Where(member =>
            {
                string name = (string?)member.Attribute("name") ?? "";
                return name.StartsWith("M:Eplan.EplApi.DataModel.", StringComparison.Ordinal)
                    && (name.IndexOf(".ConnectionDefinitionPoint.Create(", StringComparison.Ordinal) >= 0 || name.IndexOf(".PotentialDefinition.Create(", StringComparison.Ordinal) >= 0
                        || name.IndexOf(".BoxedDevice.Create(", StringComparison.Ordinal) >= 0 || name.IndexOf(".BlackBox.Create(", StringComparison.Ordinal) >= 0);
            }).Select(member => member.ToString()));
            result["managedBaseTypes"] = new JArray(new[] { typeof(ConnectionDefinitionPoint), typeof(PotentialDefinition), typeof(BoxedDevice) }
                .Select(type => new JObject { ["type"] = type.FullName, ["baseType"] = type.BaseType?.FullName }));
        }
        catch (Exception error) { result["warning"] = error.ToString(); }
        return result;
    }

    private static bool? HasDefinition(JObject observation, long id, string field)
    {
        if (observation["connections"] is not JArray connections)
        { return null; }
        if (connections.OfType<JObject>().Any(connection => connection[field] is JArray ids && ids.Values<long>().Contains(id)))
        { return true; }
        return connections.OfType<JObject>().Any(connection => connection[field] is not JArray) ? null : false;
    }

    private static bool? ConnectionPropertyEquals(JObject observation, long id, int property, string expected)
    {
        if (observation["connections"] is not JArray connections)
        { return null; }
        bool unknown = false;
        foreach (JObject connection in connections.OfType<JObject>())
        {
            if (connection["connectionDefPointIds"] is not JArray ids)
            { unknown = true; continue; }
            if (!ids.Values<long>().Contains(id))
            { continue; }
            bool? matches = PropertyEquals(connection, property, expected);
            if (matches == true)
            { return true; }
            unknown |= matches is null;
        }
        return unknown ? null : false;
    }

    private static bool? PropertyEquals(JToken? observation, int id, string expected)
    {
        if (observation?["properties"]?["values"] is not JObject values)
        { return null; }
        string key = id.ToString(CultureInfo.InvariantCulture);
        if (observation["properties"]?["errors"] is JArray errors && errors.OfType<JObject>().Any(error => error["id"] is null || (string?)error["id"] == key))
        { return null; }
        return string.Equals((string?)values[key], expected, StringComparison.Ordinal);
    }

    private static string TypeOf(JToken? observation) => (string?)observation?["type"] ?? "unavailable";
    private static bool? ReadBoolean(JToken? value) => value?.Type == JTokenType.Boolean ? (bool)value : null;
    private static string YesNo(bool? value) => value is null ? "unknown (see warnings/errors)" : value.Value ? "yes" : "no";
    private static JArray Point(PointD point) => new(point.X, point.Y);
    private static JToken Observe(Func<object?> read)
    {
        try
        { object? value = read(); return value is null ? JValue.CreateNull() : value is JToken token ? token : JToken.FromObject(value); }
        catch (Exception error) { return new JObject { ["error"] = error.ToString() }; }
    }
}
