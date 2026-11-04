using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Exports detached, read-only native page evidence for the redraw spike.</summary>
public sealed partial class PageExportAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargePageExport"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            JObject input = JObject.Parse(File.ReadAllText(file));
            string expected = (string?)input["project"] ?? throw new ArgumentException("project is required.");
            string output = (string?)input["output"] ?? throw new ArgumentException("output is required.");
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(output)
                || input["pages"] is not JArray requested || requested.Count == 0
                || requested.Any(name => name.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)name)))
            { throw new ArgumentException("Supply project, output, and nonempty full page names."); }

            // Only read the current project. Never open projects, change selection, or start a transaction.
            Project project = new SelectionSet().GetCurrentProject(false);
            if (project is null || !string.Equals(project.ProjectFullName, expected, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("The explicitly requested project must be current."); }
            try
            {
                Directory.CreateDirectory(output);
                // A native crash cannot be caught: the last line names the member being read (live 2026-11-03: 0xc0000374).
                using var trace = new StreamWriter(Path.Combine(output, "export-trace.log"), append: false) { AutoFlush = true };
                _trace = trace;
                var pages = new JArray();
                result["pages"] = pages;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in requested.Values<string>().Select(name => name!))
                {
                    JObject page = ReadPage(project, name);
                    string stem = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                    string leaf = stem + ".json";
                    int suffix = 1;
                    while (!names.Add(leaf))
                    { leaf = stem + "-" + (suffix++).ToString(CultureInfo.InvariantCulture) + ".json"; }
                    string path = Path.Combine(output, leaf);
                    File.WriteAllText(path, page.ToString());
                    pages.Add(new JObject { ["name"] = name, ["file"] = path });
                }
                result["success"] = true;
            }
            finally { GC.KeepAlive(project); }
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static JObject ReadPage(Project project, string name)
    {
        var row = new JObject { ["name"] = name, ["errors"] = new JArray(), ["placements"] = new JArray(), ["connections"] = new JArray(), ["unhandledTypes"] = new JObject() };
        Page? page = null;
        Page[] candidates = Get(row, project, "Project.Pages", () => project.Pages, []);
        foreach (Page candidate in candidates)
        {
            if (string.Equals(Get<string?>(row, candidate, "Page.Name", () => candidate.Name), name, StringComparison.Ordinal))
            { page = candidate; break; }
        }
        if (page is null)
        {
            Error(row, "Project.Pages", new InvalidOperationException("Requested page was not found: " + name));
            return row;
        }
        try
        {
            Read(row, "name", page, "Page.Name", () => page.Name);
            Read(row, "pageType", page, "Page.PageType", () => page.PageType.ToString());
            Read(row, "size", page, "Page.Size", () => Point(page.Size));
            Read(row, "gridSize", page, "Page.GridSize", () => page.GridSize);
            Read(row, "plotFrame", page, "Page.PlotFrame", () =>
            {
                using var frame = page.PlotFrame;
                return frame is null ? null : Get<string?>(row, frame, "PlotFrame.Name", () => frame.Name);
            });
            // A55/A80: Properties is cached by its owner. Keep it alive; disposing it invalidates later reads.
#pragma warning disable CA2000
            PagePropertyList? properties = Get<PagePropertyList?>(row, page, "Page.Properties", () => page.Properties);
#pragma warning restore CA2000
            if (properties is not null)
            {
                row["properties"] = PropertyMap(ReadProperties(page, properties, row));
                Read(row, "description", page, "PageProperties.PAGE_NOMINATIOMN", () =>
                {
                    using PropertyValue value = properties.PAGE_NOMINATIOMN;
                    using MultiLangString text = value.ToMultiLangString();
                    return ReadLanguages(text, row);
                });
                Read(row, "form", page, "PageProperties.PAGE_FORMULAR", () =>
                {
                    using PropertyValue value = properties.PAGE_FORMULAR;
                    return Get<string?>(row, value, "PAGE_FORMULAR.ToString", () => value.ToString());
                });
            }
            using (ReportBlock? block = Get<ReportBlock?>(row, page, "Page.ReportBlock", () => page.ReportBlock))
            {
                row["isReportPage"] = block is not null;
                row["report"] = block is null ? new JArray() : new JArray(ReadReport(block));
            }

            var placements = (JArray)row["placements"]!;
            var unhandled = (JObject)row["unhandledTypes"]!;
            foreach (Placement placement in Get(row, page, "Page.AllPlacements", () => page.AllPlacements, []))
            {
                AddPlacement(placement, placements, unhandled);
                // DataModelu.xml: AllPlacements excludes PropertyPlacement; enumerate its owners explicitly.
                PropertyPlacement[] attached = placement switch
                {
                    SymbolReference symbol => Get(row, symbol, "SymbolReference.PropertyPlacements", () => symbol.PropertyPlacements, []),
                    PlaceHolder holder => Get(row, holder, "PlaceHolder.PropertyPlacements", () => holder.PropertyPlacements, []),
                    ViewPart part => Get(row, part, "ViewPart.PropertyPlacements", () => part.PropertyPlacements, []),
                    _ => [],
                };
                foreach (PropertyPlacement property in attached)
                { AddPlacement(property, placements, unhandled); }
            }
            row["connections"] = ReadConnections(project, page, name, row);
        }
        catch (Exception error) { Error(row, "Page.Export", error); }
        finally { GC.KeepAlive(page); GC.KeepAlive(candidates); }
        return row;
    }

    private static void AddPlacement(Placement placement, JArray placements, JObject unhandled)
    {
        var row = new JObject { ["type"] = placement.GetType().Name, ["errors"] = new JArray() };
        placements.Add(row);
        try
        {
            Read(row, "oid", placement, "Placement.ObjectIdentifier", () => placement.ObjectIdentifier);
            Read(row, "location", placement, "Placement.Location", () => Point(placement.Location));
            Read(row, "boundingBox", placement, "Placement.GetBoundingBox", () => new JArray(placement.GetBoundingBox().Select(Point)));
            bool handled = ReadGraphics(placement, row);
            if (placement is Function function)
            {
                ReadFunction(function, row);
                handled = true;
            }
            else if (placement is PotentialDefinition potential)
            {
                Read(row, "potentialName", potential, "PotentialDefinition.PotentialName", () => potential.PotentialName);
                Read(row, "signalName", potential, "PotentialDefinition.SignalName", () => potential.SignalName);
                handled = true;
            }
            else if (placement is InterruptionPoint or LocationBox || placement.GetType() == typeof(SymbolReference))
            { handled = true; }
            else if (placement is Block block)
            {
                ReadBlock(block, row, unhandled);
                handled = true;
            }
            if (handled && placement is SymbolReference symbol)
            { ReadSymbol(symbol, row); }
            if (placement is InterruptionPoint interruption)
            { ReadInterruption(interruption, row); }
            if (placement is LocationBox location)
            { ReadLocationBox(location, row); }
            if (handled)
            {
#pragma warning disable CA2000 // A55/A80: the owner caches this list, including when read again as a block member.
                PlacementPropertyList? properties = Get<PlacementPropertyList?>(row, placement, "Placement.Properties", () => PropertiesOf(placement));
#pragma warning restore CA2000
                if (properties is not null)
                {
                    JArray values = ReadProperties(placement, properties, row);
                    row["properties"] = values;
                    if (placement is Function)
                    {
                        row["connectionDesignations"] = new JArray(values.OfType<JObject>().Where(value => (int?)value["id"] == 20022).Select(value => value.DeepClone()));
                        row["connectionDescriptions"] = new JArray(values.OfType<JObject>().Where(value => (int?)value["id"] == 20023).Select(value => value.DeepClone()));
                    }
                }
            }
            else
            {
                string type = placement.GetType().Name;
                unhandled[type] = ((int?)unhandled[type] ?? 0) + 1;
            }
        }
        catch (Exception error) { Error(row, "Placement.Export", error); }
        finally { GC.KeepAlive(placement); }
    }

    private static PlacementPropertyList PropertiesOf(Placement placement) => placement switch
    {
        ConnectionDefinitionPoint point => point.Properties,
        PotentialDefinition potential => potential.Properties,
        Function function => function.Properties,
        InterruptionPoint interruption => interruption.Properties,
        LocationBox location => location.Properties,
        SymbolReference symbol => symbol.Properties,
        Block block => block.Properties,
        _ => placement.Properties,
    };

    private static void ReadSymbol(SymbolReference symbol, JObject row)
    {
        var variant = Get(row, symbol, "SymbolReference.SymbolVariant", () => symbol.SymbolVariant);
        if (variant is not null)
        {
            var data = new JObject();
            row["symbol"] = data;
            Read(data, "library", variant, "SymbolVariant.SymbolLibraryName", () => variant.SymbolLibraryName);
            Read(data, "name", variant, "SymbolVariant.SymbolName", () => variant.SymbolName);
            Read(data, "variant", variant, "SymbolVariant.VariantNr", () => variant.VariantNr);
            Read(row, "variantName", variant, "SymbolVariant.VariantNr", () => ((char)('A' + variant.VariantNr)).ToString());
        }
        Read(row, "useLocalPropertyPlacements", symbol, "SymbolReference.UseLocalPropertyPlacements", () => symbol.UseLocalPropertyPlacements);
        ReadSymbolConnections(symbol, row);
        Block? block = Get<Block?>(row, symbol, "SymbolReference.Block", () => symbol.Block);
        if (block is not null)
        { Read(row, "blockOid", block, "Block.ObjectIdentifier", () => block.ObjectIdentifier); }
        GC.KeepAlive(block);
        GC.KeepAlive(variant);
        GC.KeepAlive(symbol);
    }

    private static void ReadFunction(Function function, JObject row)
    {
        Read(row, "name", function, "Function.Name", () => function.Name);
        Read(row, "visibleName", function, "Function.VisibleName", () => function.VisibleName);
        Read(row, "category", function, "Function.FunctionCategory", () => function.FunctionCategory.ToString());
        Read(row, "isMain", function, "Function.IsMainFunction", () => function.IsMainFunction);
        Function? parent = Get<Function?>(row, function, "Function.ParentFunction", () => function.ParentFunction);
        row["parentFunction"] = parent is null ? null : FunctionIdentity(parent);
        var pins = new JArray();
        row["pins"] = pins;
        foreach (Pin pin in Get(row, function, "Function.Pins", () => function.Pins, []))
        { pins.Add(ReadPin(pin)); }
        var parts = new JArray();
        row["parts"] = parts;
        foreach (ArticleReference part in Get(row, function, "Function.ArticleReferences", () => function.ArticleReferences, []))
        {
            var item = new JObject();
            parts.Add(item);
            Read(item, "part", part, "ArticleReference.PartNr", () => part.PartNr);
            Read(item, "variant", part, "ArticleReference.VariantNr", () => part.VariantNr);
            Read(item, "count", part, "ArticleReference.Count", () => part.Count);
            GC.KeepAlive(function);
        }
        if (function is BoxedDevice box && function is not ConnectionDefinitionPoint)
        {
            // A86: CDP inherits this API but GetLogicalArea throws NotImplementedException.
            Read(row, "logicalArea", box, "BoxedDevice.GetLogicalArea", () =>
            {
                RectangleD area = box.GetLogicalArea();
                return new JObject { ["lowerLeft"] = Point(area.LowerLeft), ["upperRight"] = Point(area.UpperRight) };
            });
            var inside = new JArray();
            row["functionsInside"] = inside;
            foreach (Function child in Get(row, box, "BoxedDevice.FunctionsInside", () => box.FunctionsInside, []))
            { inside.Add(FunctionIdentity(child)); }
        }
        GC.KeepAlive(parent);
        GC.KeepAlive(function);
    }

    private static JObject FunctionIdentity(Function function)
    {
        var row = new JObject();
        Read(row, "oid", function, "Function.ObjectIdentifier", () => function.ObjectIdentifier);
        Read(row, "name", function, "Function.Name", () => function.Name);
        return row;
    }

    private static JObject ReadPin(Pin pin)
    {
        var row = new JObject();
        Read(row, "name", pin, "Pin.Name", () => pin.Name);
        Read(row, "designation", pin, "Pin.Designation", () => pin.Designation);
        Read(row, "index", pin, "Pin.Index", () => pin.Index);
        Read(row, "direction", pin, "Pin.Direction", () => pin.Direction.ToString());
        return row;
    }

    private static JArray ReadConnections(Project project, Page page, string name, JObject pageRow)
    {
        var rows = new JArray();
        using var finder = new DMObjectsFinder(project);
        foreach (Connection connection in Get(pageRow, finder, "DMObjectsFinder.GetConnections", () => finder.GetConnections(null), []))
        {
            var row = new JObject();
            try
            {
                Page? owner = Get<Page?>(row, connection, "Connection.Page", () => connection.Page);
                string? ownerName = owner is null ? null : Get<string?>(row, owner, "Connection.Page.Name", () => owner.Name);
                if (ownerName is not null && !string.Equals(ownerName, name, StringComparison.Ordinal))
                { continue; }
                // A failed page lookup is visible once, rather than silently dropping the connection.
                if (ownerName is null)
                {
                    foreach (JToken error in (JArray?)row["errors"] ?? [])
                    { ((JArray)pageRow["errors"]!).Add(error.DeepClone()); }
                    continue;
                }
                rows.Add(row);
                Read(row, "oid", connection, "Connection.ObjectIdentifier", () => connection.ObjectIdentifier);
                Read(row, "start", connection, "Connection.StartPin", () => Endpoint(connection.StartPin));
                Read(row, "end", connection, "Connection.EndPin", () => Endpoint(connection.EndPin));
#pragma warning disable CA2000 // A55/A80: do not dispose the owner's cached Properties list.
                UniversalPropertyList? properties = Get<UniversalPropertyList?>(row, connection, "Connection.Properties", () => connection.Properties);
#pragma warning restore CA2000
                if (properties is not null)
                { row["properties"] = ReadProperties(connection, properties, row, id => id >= 31000 && id < 32000); }
                GC.KeepAlive(owner);
            }
            catch (Exception error) { Error(row, "Connection.Export", error); }
            finally { GC.KeepAlive(connection); }
        }
        GC.KeepAlive(page);
        GC.KeepAlive(project);
        return rows;
    }

    private static JObject? Endpoint(Pin? pin)
    {
        if (pin is null)
        { return null; }
        JObject row = ReadPin(pin);
        row["pinName"] = row["name"]?.DeepClone();
        row["pinIndex"] = row["index"]?.DeepClone();
        Function? owner = Get<Function?>(row, pin, "Pin.ParentFunction", () => pin.ParentFunction);
        if (owner is not null)
        {
            Read(row, "name", owner, "Function.Name", () => owner.Name);
            Read(row, "oid", owner, "Function.ObjectIdentifier", () => owner.ObjectIdentifier);
        }
        GC.KeepAlive(pin);
        return row;
    }

    private static JObject ReadReport(ReportBlock block)
    {
        var row = new JObject();
        Read(row, "type", block, "ReportBlock.Type", () => block.Type.ToString());
        Read(row, "formName", block, "ReportBlock.FormName", () => block.FormName);
        Read(row, "filterSchemaName", block, "ReportBlock.FilterSchemaName", () => block.FilterSchemaName);
        Read(row, "filterSchemaData", block, "ReportBlock.FilterSchemaData", () => block.FilterSchemaData);
        Read(row, "sortSchemaName", block, "ReportBlock.SortSchemaName", () => block.SortSchemaName);
        Read(row, "sortSchemaData", block, "ReportBlock.SortSchemaData", () => block.SortSchemaData);
        Read(row, "functionFilterSchemaName", block, "ReportBlock.FunctionFilterSchemaName", () => block.FunctionFilterSchemaName);
        Read(row, "functionFilterSchemaData", block, "ReportBlock.FunctionFilterSchemaData", () => block.FunctionFilterSchemaData);
        Read(row, "functionSortSchemaName", block, "ReportBlock.FunctionSortSchemaName", () => block.FunctionSortSchemaName);
        Read(row, "functionSortSchemaData", block, "ReportBlock.FunctionSortSchemaData", () => block.FunctionSortSchemaData);
        Read(row, "deviceTag", block, "ReportBlock.DeviceTag", () => block.DeviceTag);
        Read(row, "isAutomaticPageDescription", block, "ReportBlock.IsAutomaticPageDescription", () => block.IsAutomaticPageDescription);
        Read(row, "manualPageDescription", block, "ReportBlock.ManualPageDescription", () =>
        {
            using MultiLangString text = block.ManualPageDescription;
            return text is null ? null : ReadLanguages(text, row);
        });
        var parts = new JArray();
        row["deviceTagNameParts"] = parts;
        foreach (FunctionBasePropertyList part in Get(row, block, "ReportBlock.DeviceTagNameParts", () => block.DeviceTagNameParts, []))
        {
            parts.Add(ReadProperties(block, part, row));
        }
        return row;
    }

    private static JArray ReadProperties(object owner, UniversalPropertyList properties, JObject row, Func<int, bool>? include = null)
    {
        var values = new JArray();
        AnyPropertyId[] ids = Get(row, owner, "Properties.ExistingIds", () => properties.ExistingIds, []);
        try
        {
            foreach (AnyPropertyId id in ids)
            {
                int? number = Get<int?>(row, id, "AnyPropertyId.AsInt", () => id.AsInt);
                if (number is null || (include is not null && !include(number.Value)))
                { continue; }
                string member = "Properties[" + number.Value.ToString(CultureInfo.InvariantCulture) + "]";
                using PropertyValue? value = Get<PropertyValue?>(row, owner, member, () => properties[id]);
                if (value is null)
                { continue; }
                using PropertyDefinition? definition = Get<PropertyDefinition?>(row, value, member + ".Definition", () => value.Definition);
                bool indexed = definition is not null && Get(row, definition, member + ".IsIndexed", () => definition.IsIndexed);
                PropertyDefinition.PropertyType? type = definition is null ? null
                    : Get<PropertyDefinition.PropertyType?>(row, definition, member + ".Type", () => definition.Type);
                int[] indexes = indexed ? Get(row, value, member + ".Indexes", () => value.Indexes, []) : [0];
                foreach (int index in indexes)
                {
                    if (indexed)
                    {
                        using PropertyValue? item = Get<PropertyValue?>(row, value, member + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", () => value[index]);
                        if (item is not null)
                        { AddProperty(values, row, item, number.Value, index, member, type); }
                    }
                    else
                    { AddProperty(values, row, value, number.Value, 0, member, type); }
                }
            }
        }
        finally
        {
            foreach (AnyPropertyId id in ids)
            { id.Dispose(); }
            GC.KeepAlive(properties);
            // A80: release values before the owner becomes collectible; its cached list stays owner-managed.
            GC.KeepAlive(owner);
        }
        return values;
    }

    private static void AddProperty(JArray values, JObject row, PropertyValue value, int id, int index, string member, PropertyDefinition.PropertyType? type)
    {
        bool? empty = Get<bool?>(row, value, member + ".IsEmpty", () => value.IsEmpty);
        if (empty == true)
        { return; }
        if (type == PropertyDefinition.PropertyType.MultilangString)
        {
            using MultiLangString? languages = Get<MultiLangString?>(row, value, member + ".ToMultiLangString", () => value.ToMultiLangString());
            if (languages is not null)
            { values.Add(new JObject { ["id"] = id, ["index"] = index, ["value"] = ReadLanguages(languages, row) }); }
            return;
        }
        string? text = Get<string?>(row, value, member + ".ToString", () => value.ToString());
        if (!string.IsNullOrEmpty(text))
        { values.Add(new JObject { ["id"] = id, ["index"] = index, ["value"] = text }); }
    }

    private static JObject PropertyMap(JArray properties)
    {
        var result = new JObject();
        foreach (IGrouping<int, JObject> group in properties.OfType<JObject>().GroupBy(value => (int)value["id"]!))
        {
            string key = group.Key.ToString(CultureInfo.InvariantCulture);
            result[key] = group.Count() == 1 && (int)group.First()["index"]! == 0
                ? group.First()["value"]!.DeepClone()
                : new JObject(group.Select(value => new JProperty(((int)value["index"]!).ToString(CultureInfo.InvariantCulture), value["value"]!.DeepClone())));
        }
        return result;
    }

    private static JObject ReadLanguages(MultiLangString value, JObject row)
    {
        var result = new JObject();
        var languages = new LanguageList();
        int count = Get(row, value, "MultiLangString.GetLanguageList", () => value.GetLanguageList(ref languages));
        for (int index = 0; index < count; index++)
        {
            int current = index;
            ISOCode.Language? language = Get<ISOCode.Language?>(row, languages, "LanguageList.Language", () => languages.get_Language(current));
            if (language is null)
            { continue; }
            using var code = new ISOCode(language.Value);
            string? name = Get<string?>(row, code, "ISOCode.GetString", () => code.GetString());
            if (name is not null)
            { result[name] = Get<string?>(row, value, "MultiLangString.GetString[" + name + "]", () => value.GetString(language.Value)); }
        }
        GC.KeepAlive(value);
        return result;
    }

    private static JObject Point(PointD point) => new() { ["x"] = point.X, ["y"] = point.Y };

    private static void Read(JObject row, string key, object owner, string member, Func<object?> read)
    {
        object? value = Get<object?>(row, owner, member, read);
        row[key] = value is null ? JValue.CreateNull() : value is JToken token ? token : JToken.FromObject(value);
    }

    private static StreamWriter? _trace;

    private static T Get<T>(JObject row, object owner, string member, Func<T> read, T fallback = default!)
    {
        _trace?.WriteLine((string?)row["type"] + " " + (string?)row["oid"] + " " + member);
        try
        { return read(); }
        catch (Exception error) { Error(row, member, error); return fallback; }
        finally { GC.KeepAlive(owner); }
    }

    private static void Error(JObject row, string member, Exception error) =>
        ((JArray)(row["errors"] ??= new JArray())).Add(new JObject { ["member"] = member, ["error"] = error.ToString() });
}
