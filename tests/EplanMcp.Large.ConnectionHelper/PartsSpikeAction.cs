using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Observes parts and native function templates independently of product placement decisions.</summary>
public sealed class PartsSpikeAction : IEplAction
{
    private static readonly int[] _propertyIds = [20022, 20023, 20028, 20029, 20400, 20402, 20406, 20011];
    private static readonly string[] _partPropertyWords = ["FUNCTION", "TEMPLATE", "MACRO", "PLC", "CONNECTION"];

    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargePartsSpike"; Ordinal = 20; return true; }
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
            if (!string.Equals(project.ProjectFullName, Path.Combine(home, "scratch", "writes", "LargeWrite"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase)
                || !AllowedPage((string?)input["page"]))
            { throw new InvalidOperationException("Only an explicit new page of current scratch LargeWrite is permitted."); }

            result["createTerminalDeviceSignatures"] = new JArray(
                "CreateTerminalDevice(Project, string fullDeviceTag, string partNumber, string partVariant, string numberingPattern, bool numberByLevels)",
                "CreateTerminalDevice(Project, string fullDeviceTag, string partNumber, string partVariant, string numberingPattern, int terminalDevicePosition, int terminalSortCode, bool numberByLevels)");
            switch ((string?)input["mode"])
            {
                case "templates":
                    Templates(project, (JArray)input["parts"]!, result);
                    break;
                case "place":
                    Place(project, input, result);
                    break;
                case "templates-place":
                    TemplatesPlace(project, input, result);
                    break;
                case "place-unplaced":
                    PlaceUnplaced(project, input, result);
                    break;
                case "rename-unplaced":
                    RenameUnplaced(project, input, result);
                    break;
                case "device-list":
                    DeviceList(project, input, result);
                    break;
                case "assign":
                    Assign(project, input, result);
                    break;
                default:
                    throw new ArgumentException("Unknown parts spike mode.");
            }
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = error.ToString(); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static bool AllowedPage(string? name) => name?.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal) == true;

    private static void Templates(Project project, JArray requests, JObject result)
    {
        var parts = new JArray();
        result["parts"] = parts;
        // A82: record master-data facts without calling any database writer.
        foreach (JObject request in requests.Cast<JObject>())
        {
            var part = new JObject { ["part"] = request["part"], ["variant"] = request["variant"] };
            parts.Add(part);
            try
            { part["database"] = ReadPart((string)request["part"]!, (string?)request["variant"] ?? ""); }
            catch (Exception error) { part["databaseError"] = error.ToString(); }
        }
        using var scope = new TransactionScope("Parts template spike");
        using var service = new DeviceService();
        foreach (JObject part in parts.Cast<JObject>())
        {
            try
            {
                using var properties = new FunctionPropertyList();
                var functions = new List<IFunctionBase>();
                // A70/A82: this parameter is ref, not out; start with a new empty list.
                service.CreateDevice(project, (string)part["part"]!, (string?)part["variant"] ?? "", properties, ref functions);
                part["functions"] = new JArray(functions.Select((function, index) => ReadFunction(function, index)));
            }
            catch (Exception error) { part["error"] = error.ToString(); }
        }
        // A7: disposing this uncommitted scope rolls back every device and template created above.
    }

    private static JObject ReadPart(string number, string variant)
    {
        using MDPartsDatabase database = new MDPartsManagement().OpenDatabase() ?? throw new InvalidOperationException("EPLAN opened no parts database.");
        try
        {
            MDPart[] variants = database.GetPartVariants(number) ?? [];
            var result = new JObject
            {
                ["variants"] = new JArray(variants.Select(part => new JObject { ["part"] = part.PartNr, ["variant"] = part.Variant })),
                ["requestedVariant"] = variant,
                ["parts"] = new JArray(variants.Select(ReadPartVariant))
            };
            return result;
        }
        finally { database.Close(); }
    }

    private static JObject ReadPartVariant(MDPart part)
    {
        var warnings = new JArray();
        var properties = new JObject { ["ARTICLE_MACRO"] = null };
        var result = new JObject { ["part"] = part.PartNr, ["variant"] = part.Variant, ["properties"] = properties, ["warnings"] = warnings };
        using MDPartsDatabaseItemPropertyList list = part.Properties;
        MDAnyPropertyId[] ids = [];
        try
        {
            try
            {
                using MDPropertyValue macro = list.ARTICLE_MACRO;
                if (!macro.IsEmpty)
                { properties["ARTICLE_MACRO"] = macro.ToString(); }
            }
            catch (Exception error) { Warn(warnings, "ARTICLE_MACRO", error); }
            // A82: the existing identifiers also include indexed fields without a simple typed getter.
            ids = list.ExistingIds;
            foreach (MDAnyPropertyId id in ids)
            {
                if (id.Id == 22010)
                { continue; }
                string propertyName = id.Id.ToString(CultureInfo.InvariantCulture);
                try
                {
                    using MDPropertyValue value = list[id];
                    using MDPropertyDefinition definition = value.Definition;
                    propertyName = Enum.GetName(typeof(Eplan.EplApi.MasterData.Properties.MDPartsDatabaseItem), id.Id) ?? definition.Name;
                    if (!_partPropertyWords.Any(word => propertyName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
                    { continue; }
                    if (definition.IsIndexed)
                    {
                        var indexes = new JObject();
                        foreach (int index in value.Indexes)
                        {
                            try
                            {
                                using MDPropertyValue item = value[index];
                                if (!item.IsEmpty && item.ToString() is { Length: > 0 } text)
                                { indexes[index.ToString(CultureInfo.InvariantCulture)] = text; }
                            }
                            catch (Exception error) { Warn(warnings, propertyName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", error); }
                        }
                        if (indexes.HasValues)
                        { properties[propertyName] = indexes; }
                    }
                    else if (!value.IsEmpty && value.ToString() is { Length: > 0 } text)
                    { properties[propertyName] = text; }
                }
                catch (Exception error) { Warn(warnings, propertyName, error); }
            }
        }
        finally
        {
            foreach (MDAnyPropertyId id in ids)
            { id.Dispose(); }
            GC.KeepAlive(part);
        }
        return result;
    }

    private static void Place(Project project, JObject input, JObject result)
    {
        var placements = new JArray();
        result["placements"] = placements;
        var prepared = new List<(JObject Request, JObject Result, SymbolVariant? Variant)>();
        foreach (JObject request in ((JArray)input["placements"]!).Cast<JObject>())
        {
            if (!AllowedPage((string?)request["page"]))
            { throw new InvalidOperationException("Every placement page must belong to the explicit LargeWrite page range."); }
            var placement = new JObject { ["request"] = request.DeepClone(), ["success"] = false };
            placements.Add(placement);
            SymbolVariant? variant = null;
            // A52: resolve symbol library objects before entering SafetyPoint.
            try
            {
                variant = project.SymbolLibraries.Single(library => library.Name == (string)request["symbolLibrary"]!)
                    .Symbols.Single(symbol => symbol.Name == (string)request["symbolName"]!)
                    .Variants.Single(symbol => symbol.VariantNr == (int)request["symbolVariant"]!);
            }
            catch (Exception error) { placement["error"] = error.ToString(); }
            prepared.Add((request, placement, variant));
        }

        string name = (string)input["tag"]!;
        using (var scope = new TransactionScope("Parts placement spike"))
        {
            try
            {
                using var service = new DeviceService();
                using var properties = new FunctionPropertyList();
                using var additional = new FunctionPropertyList();
                // A82: full-name properties are computed. Parse using an observed category, never a guessed default.
                if (input["functionCategory"]?.Type != JTokenType.String)
                { throw new ArgumentException("An observed functionCategory from templates is required to parse the requested full tag."); }
                var category = (FunctionCategory)Enum.Parse(typeof(FunctionCategory), (string)input["functionCategory"]!, false);
                project.ParseNameAndProps((int)category, name, properties, additional);
                var nameWarnings = new JArray();
                result["nameInput"] = new JObject { ["tag"] = name, ["functionCategory"] = category.ToString(), ["properties"] = ReadNameProperties(properties, nameWarnings), ["warnings"] = nameWarnings };
                var functions = new List<IFunctionBase>();
                service.CreateDevice(project, (string)input["part"]!, (string?)input["variant"] ?? "", properties, ref functions);
                foreach ((JObject request, JObject placement, SymbolVariant? symbol) in prepared)
                {
                    if (symbol is null)
                    { continue; }
                    try
                    {
                        Page page = project.Pages.Single(candidate => candidate.Name == (string)request["page"]!);
                        var documentType = (DocumentTypeManager.DocumentType)Enum.Parse(typeof(DocumentTypeManager.DocumentType), (string)request["documentType"]!, false);
                        if (functions[(int)request["functionIndex"]!] is not Function function)
                        { throw new InvalidOperationException("The requested template is not a two-dimensional Function."); }
                        function.PlaceAt(page, new PointD((double)request["x"]!, (double)request["y"]!), documentType, symbol);
                        placement["success"] = true;
                    }
                    catch (Exception error) { placement["error"] = error.ToString(); }
                }
                scope.Commit();
                scope.CloseUndo();
            }
            catch { scope.Rollback(); throw; }
        }
        // A79: read through fresh wrappers after the committed scope has ended.
        var readbackWarnings = new JArray();
        result["functions"] = ReadDevice(project, name, readbackWarnings);
        result["readbackWarnings"] = readbackWarnings;
    }

    private static void TemplatesPlace(Project project, JObject input, JObject result)
    {
        var requests = (JArray)input["templatePlacements"]!;
        if (requests.Count > 2)
        { throw new ArgumentException("At most two template placements are permitted."); }
        var warnings = new JArray();
        var attempts = new JArray();
        result["warnings"] = warnings;
        result["templatePlacements"] = attempts;
        result["templatesBefore"] = new JArray();
        result["templatesAfter"] = new JArray();
        string? deviceTag = null;
        using (var scope = new TransactionScope("Parts template placement spike"))
        {
            try
            {
                Page page = project.Pages.Single(candidate => candidate.Name == (string)input["page"]!);
                using var service = new DeviceService();
                // HEServices XML: this overload places the main function and leaves other template functions unplaced.
                Function[] created = service.CreateDevice((string)input["part"]!, (string?)input["variant"] ?? "", page,
                    new PointD((double)input["x"]!, (double)input["y"]!));
                var observations = created.Select((function, index) => ReadFunction(function, index)).ToArray();
                result["created"] = new JArray(observations);
                int mainIndex = Array.FindIndex(observations, observation => (bool?)observation["isMainFunction"] == true);
                if (mainIndex < 0)
                { mainIndex = Array.FindIndex(observations, observation => (bool?)observation["isPlaced"] == true); }
                result["main"] = mainIndex < 0 ? null : observations[mainIndex]["name"]?.DeepClone();
                if (mainIndex >= 0)
                {
                    Function main = created[mainIndex];
                    deviceTag = (string?)observations[mainIndex]["deviceTag"];
                    // DataModel XML: the main function exposes transient device templates through FunctionTemplates.
                    StorableObject[] templates = ReadTemplates(main, result, "templatesBefore", warnings);
                    JArray? before = result["templatesBefore"] as JArray;
                    int placementIndex = 0;
                    for (int index = 0; index < templates.Length && placementIndex < requests.Count; index++)
                    {
                        if (templates[index] is not Function template || before?[index] is not JObject observation
                            || (bool?)observation["isTemplate"] != true || (bool?)observation["isCoveredTemplate"] != false)
                        { continue; }
                        var request = (JObject)requests[placementIndex++]!;
                        var attemptWarnings = new JArray();
                        var attempt = new JObject { ["index"] = index, ["request"] = request.DeepClone(), ["success"] = false, ["error"] = null, ["warnings"] = attemptWarnings };
                        attempts.Add(attempt);
                        SymbolVariant? symbol = null;
                        Observe(attempt, "symbol", () => ReadSymbol(symbol = template.SymbolVariant), attemptWarnings);
                        Observe(attempt, "symbolIsValid", () => symbol is not null && symbol.IsValid, attemptWarnings);
                        if ((bool?)attempt["symbolIsValid"] != true)
                        {
                            attempt["error"] = "The template SymbolVariant is null, invalid or unreadable; placement was skipped.";
                            continue;
                        }
                        try
                        {
                            // DataModel XML: PlaceAt instantiates a transient template; an instantiated function would be duplicated.
                            template.PlaceAt(page, new PointD((double)request["x"]!, (double)request["y"]!), DocumentTypeManager.DocumentType.Circuit, symbol!);
                            attempt["success"] = true;
                        }
                        catch (Exception error) { attempt["error"] = error.ToString(); }
                    }
                    GC.KeepAlive(main);
                }
                else
                { warnings.Add("CreateDevice returned no main or placed function; template placement was skipped."); }
                scope.Commit();
                scope.CloseUndo();
            }
            catch { scope.Rollback(); throw; }
        }
        // A79: retain only the device tag across the scope and obtain fresh function wrappers for read-back.
        var readbackWarnings = new JArray();
        result["readbackWarnings"] = readbackWarnings;
        JArray functions = ReadDeviceByTag(project, deviceTag, readbackWarnings);
        result["functions"] = functions;
        ReadMainTemplates(project, deviceTag, result, readbackWarnings);
        result["duplicates"] = ReadDuplicates(functions);
    }

    private static JArray ReadDuplicates(JArray functions) => new(functions.OfType<JObject>().Where(function => function["name"]?.Type == JTokenType.String)
            .GroupBy(function => (string)function["name"]!, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => new JObject { ["name"] = group.Key, ["isPlaced"] = new JArray(group.Select(function => function["isPlaced"]?.DeepClone())) }));

    private static void PlaceUnplaced(Project project, JObject input, JObject result)
    {
        var requests = (JArray)input["placements"]!;
        if (requests.Count > 2)
        { throw new ArgumentException("At most two unplaced function placements are permitted."); }
        var warnings = new JArray();
        var attempts = new JArray();
        result["warnings"] = warnings;
        result["placements"] = attempts;
        string? deviceTag = null;
        using (var scope = new TransactionScope("Parts unplaced function spike"))
        {
            try
            {
                Page? page = null;
                Observe(result, "page", () => (page = project.Pages.Single(candidate => candidate.Name == (string)input["page"]!)).Name, warnings);
                if (page is null)
                { return; }
                using var service = new DeviceService();
                using var propertyScope = new FunctionObservationScope();
                // HEServices XML: this overload places the main function and leaves the other functions unplaced.
                Function[] created = service.CreateDevice((string)input["part"]!, (string?)input["variant"] ?? "", page,
                    new PointD((double)input["x"]!, (double)input["y"]!));
                JObject[] observations = created.Select((function, index) => ReadFunction(function, index, propertyScope: propertyScope)).ToArray();
                result["created"] = new JArray(observations);
                int mainIndex = Array.FindIndex(observations, observation => (bool?)observation["isMainFunction"] == true);
                if (mainIndex < 0)
                { mainIndex = Array.FindIndex(observations, observation => (bool?)observation["isPlaced"] == true); }
                result["main"] = mainIndex < 0 ? null : observations[mainIndex]["name"]?.DeepClone();
                if (mainIndex >= 0)
                { deviceTag = (string?)observations[mainIndex]["deviceTag"]; }
                int placementIndex = 0;
                for (int index = 0; index < created.Length && placementIndex < requests.Count; index++)
                {
                    if ((bool?)observations[index]["isPlaced"] != false)
                    { continue; }
                    var request = (JObject)requests[placementIndex++]!;
                    var attemptWarnings = new JArray();
                    var attempt = new JObject
                    {
                        ["index"] = index,
                        ["request"] = request.DeepClone(),
                        ["input"] = observations[index].DeepClone(),
                        ["success"] = false,
                        ["error"] = null,
                        ["warnings"] = attemptWarnings
                    };
                    attempts.Add(attempt);
                    try
                    {
                        Function function = created[index];
                        // DataModel XML: Place returns a new placed function; only an uncovered template becomes persistent.
                        Function placed = function.Place(page, new PointD((double)request["x"]!, (double)request["y"]!), true, true, true);
                        attempt["placed"] = ReadFunction(placed, index, propertyScope: propertyScope);
                        attempt["referenceEquals"] = ReferenceEquals(function, placed);
                        Observe(attempt, "objectIdentifierEquals", () => function.ObjectIdentifier == placed.ObjectIdentifier, attemptWarnings);
                        attempt["success"] = true;
                        GC.KeepAlive(function);
                        GC.KeepAlive(placed);
                    }
                    catch (Exception error) { attempt["error"] = error.ToString(); throw; }
                }
                if (placementIndex < requests.Count)
                { warnings.Add("Fewer readable unplaced functions were returned than requested placements."); }
                // A79/A80: release cached property lists before either commit or rollback.
                propertyScope.Dispose();
                scope.Commit();
                scope.CloseUndo();
            }
            catch { scope.Rollback(); throw; }
        }
        // A79: only the device tag crosses the committed scope; read back through fresh wrappers.
        var readbackWarnings = new JArray();
        result["readbackWarnings"] = readbackWarnings;
        JArray functions = ReadDeviceByTag(project, deviceTag, readbackWarnings);
        result["functions"] = functions;
        result["duplicates"] = ReadDuplicates(functions);
    }

    private static void RenameUnplaced(Project project, JObject input, JObject result)
    {
        string tag = (string)input["tag"]!;
        string strategy = (string)input["strategy"]!;
        if (strategy is not ("name" or "properties"))
        { throw new ArgumentException("The rename strategy must be name or properties."); }
        var warnings = new JArray();
        var inside = new JArray();
        var changes = new JArray();
        var parsedNames = new JArray();
        var oldTags = new HashSet<string>(StringComparer.Ordinal);
        result["tag"] = tag;
        result["strategy"] = strategy;
        result["warnings"] = warnings;
        result["inside"] = inside;
        result["changes"] = changes;
        result["parsedNames"] = parsedNames;
        using (var scope = new TransactionScope("Parts unplaced rename spike"))
        {
            try
            {
                Page? page = null;
                Observe(result, "page", () => (page = project.Pages.Single(candidate => candidate.Name == (string)input["page"]!)).Name, warnings);
                if (page is null)
                { return; }
                using var service = new DeviceService();
                using var propertyScope = new FunctionObservationScope();
                var parsed = new Dictionary<int, FunctionPropertyList>();
                try
                {
                    Function[] created = service.CreateDevice((string)input["part"]!, (string?)input["variant"] ?? "", page,
                        new PointD((double)input["x"]!, (double)input["y"]!));
                    JObject[] before = created.Select((function, index) => ReadRenameFunction(function, index, propertyScope)).ToArray();
                    result["before"] = new JArray(before);
                    foreach (JObject observation in before)
                    {
                        if ((string?)observation["deviceTag"] is { Length: > 0 } oldTag)
                        { oldTags.Add(oldTag); }
                    }
                    // A13: rename the main function first, then every returned auxiliary function.
                    foreach (int index in Enumerable.Range(0, created.Length).OrderByDescending(index => (bool?)before[index]["isMainFunction"] == true))
                    {
                        Function function = created[index];
                        var changeWarnings = new JArray();
                        var change = new JObject { ["index"] = index, ["success"] = false, ["error"] = null, ["warnings"] = changeWarnings };
                        changes.Add(change);
                        try
                        {
                            if (strategy == "name")
                            {
                                string? oldName = (string?)before[index]["name"];
                                string? oldTag = (string?)before[index]["deviceTag"];
                                if (oldName is null || oldTag is null)
                                { changeWarnings.Add("The old name or device tag is unreadable; this rename was skipped."); }
                                else
                                {
                                    string suffix = oldTag.Length > 0 && oldName.StartsWith(oldTag, StringComparison.Ordinal) ? oldName.Substring(oldTag.Length) : "";
                                    change["name"] = tag + suffix;
                                    function.Name = tag + suffix;
                                    change["success"] = true;
                                }
                            }
                            else
                            { RenameProperties(project, function, tag, parsed, parsedNames, propertyScope, change, changeWarnings); }
                            inside.Add(ReadRenameFunction(function, index, propertyScope));
                        }
                        catch (Exception error) { change["error"] = error.ToString(); throw; }
                    }
                }
                finally
                {
                    foreach (FunctionPropertyList properties in parsed.Values)
                    { properties.Dispose(); }
                }
                // A79/A80: release cached property lists before either commit or rollback.
                propertyScope.Dispose();
                scope.Commit();
                scope.CloseUndo();
            }
            catch { scope.Rollback(); throw; }
        }

        // A79: query fresh wrappers and retain old prefixes to find detached auxiliaries.
        var after = new JArray();
        var readbackWarnings = new JArray();
        result["oldDeviceTags"] = new JArray(oldTags);
        result["readbackWarnings"] = readbackWarnings;
        bool complete = false;
        bool namesReadable = true;
        Observe(result, "after", () =>
        {
            using var finder = new DMObjectsFinder(project);
            foreach (Function function in finder.GetFunctions(null))
            {
                var identity = new JObject();
                Observe(identity, "name", () => function.Name, readbackWarnings);
                if ((string?)identity["name"] is not { } name)
                { namesReadable = false; continue; }
                if (name.StartsWith(tag, StringComparison.Ordinal) || oldTags.Any(oldTag => name.StartsWith(oldTag, StringComparison.Ordinal)))
                { after.Add(ReadRenameFunction(function, null)); }
            }
            complete = true;
            return after;
        }, readbackWarnings);
        result["after"] = after;
        bool known = complete && namesReadable && after.Count > 0 && after.All(function => function["deviceTag"]?.Type == JTokenType.String);
        result["allDeviceTagsMatch"] = known ? after.All(function => string.Equals((string?)function["deviceTag"], tag, StringComparison.Ordinal)) : (bool?)null;
        if (!known)
        { readbackWarnings.Add("The complete device-tag comparison is unavailable from the read-back observations."); }
    }

    private static void RenameProperties(Project project, Function function, string tag, Dictionary<int, FunctionPropertyList> parsed,
        JArray parsedNames, FunctionObservationScope propertyScope, JObject change, JArray warnings)
    {
        Observe(change, "category", () => (int)function.FunctionCategory, warnings);
        if (change["category"]?.Type != JTokenType.Integer)
        { warnings.Add("The function category is unreadable; property writes were skipped."); return; }
        int category = (int)change["category"]!;
        if (!parsed.TryGetValue(category, out FunctionPropertyList? properties))
        {
            properties = new FunctionPropertyList();
            parsed.Add(category, properties);
            using var additional = new FunctionPropertyList();
            var nameInput = new JObject { ["category"] = category, ["tag"] = tag, ["error"] = null };
            parsedNames.Add(nameInput);
            // A82: parse once per observed category; copy identifying properties instead of assigning Name.
            try
            { project.ParseNameAndProps(category, tag, properties, additional); }
            catch (Exception error) { nameInput["error"] = error.ToString(); throw; }
        }
        FunctionPropertyList? target = null;
        Observe(change, "propertiesReadable", () => { target = propertyScope.GetProperties(function); return true; }, warnings);
        if (target is null)
        { return; }
        AnyPropertyId[] ids = [];
        Observe(change, "propertyCount", () => { ids = properties.ExistingIds; return ids.Length; }, warnings);
        var writes = new JArray();
        change["writes"] = writes;
        try
        {
            foreach (AnyPropertyId id in ids)
            {
                var write = new JObject { ["success"] = false, ["error"] = null };
                writes.Add(write);
                Observe(write, "id", () => id.AsString, warnings);
                PropertyValue? value = null;
                try
                {
                    Observe(write, "valueReadable", () => { value = properties[id]; return true; }, warnings);
                    if (value is null)
                    { warnings.Add("A parsed property is unreadable; its write was skipped."); continue; }
                    Observe(write, "value", () => value.IsEmpty ? null : value.ToString(), warnings);
                    try
                    { target[id] = value; write["success"] = true; }
                    catch (Exception error) { write["error"] = error.ToString(); throw; }
                }
                finally { value?.Dispose(); }
            }
            change["success"] = writes.Count > 0 && writes.All(write => (bool?)write["success"] == true);
        }
        finally
        {
            foreach (AnyPropertyId id in ids)
            { id.Dispose(); }
            GC.KeepAlive(function);
        }
    }

    private static JObject ReadRenameFunction(Function function, int? index, FunctionObservationScope? propertyScope = null)
    {
        var warnings = new JArray();
        var result = new JObject { ["index"] = index, ["warnings"] = warnings };
        Observe(result, "name", () => function.Name, warnings);
        Observe(result, "isPlaced", () => function.IsPlaced, warnings);
        Observe(result, "isMainFunction", () => function.IsMainFunction, warnings);
        Observe(result, "page", () => function.Page?.Name, warnings);
        FunctionPropertyList? properties = null;
        try
        {
            Observe(result, "deviceTag", () =>
            {
                properties = propertyScope?.GetProperties(function) ?? function.Properties;
                using PropertyValue device = properties.FUNC_IDENTDEVICETAG;
                return device.IsEmpty ? null : device.ToString();
            }, warnings);
        }
        finally
        {
            if (propertyScope is null)
            { properties?.Dispose(); }
            GC.KeepAlive(function);
        }
        return result;
    }

    private static void DeviceList(Project project, JObject input, JObject result)
    {
        var requests = (JArray)input["placements"]!;
        if (requests.Count > 3)
        { throw new ArgumentException("At most three device-list template placements are permitted."); }
        var warnings = new JArray();
        var attempts = new JArray();
        result["warnings"] = warnings;
        result["placements"] = attempts;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var deviceTags = new HashSet<string>(StringComparer.Ordinal);
        using (var scope = new TransactionScope("Parts device-list spike"))
        {
            try
            {
                Page? page = null;
                Observe(result, "page", () => (page = project.Pages.Single(candidate => candidate.Name == (string)input["page"]!)).Name, warnings);
                if (page is null)
                { return; }
                using var service = new DeviceService();
                using var propertyScope = new FunctionObservationScope();
                // HEServices XML: CreateDeviceListItem adds the part to the project's device list.
                DeviceListEntry item = service.CreateDeviceListItem(project, (string)input["part"]!, (string?)input["variant"] ?? "");
                var returned = new JObject { ["type"] = item?.GetType().FullName };
                result["deviceListItem"] = returned;
                Observe(returned, "value", () => item?.ToString(), warnings);
                bool matched = false;
                foreach ((JObject observation, StorableObject[] templates) in ReadDeviceList(service, project, result, "deviceList", warnings, propertyScope))
                {
                    if (!string.Equals((string?)observation["partNumber"], (string)input["part"]!, StringComparison.Ordinal)
                        || !string.Equals((string?)observation["variant"], (string?)input["variant"] ?? "", StringComparison.Ordinal))
                    { continue; }
                    matched = true;
                    var before = (JArray?)observation["templates"];
                    int placementIndex = 0;
                    foreach (int index in Enumerable.Range(0, templates.Length).OrderByDescending(index => (bool?)before?[index]?["isMainFunction"] == true))
                    {
                        if (placementIndex >= requests.Count)
                        { break; }
                        if (templates[index] is not Function template || before?[index] is not JObject templateObservation)
                        { continue; }
                        var request = (JObject)requests[placementIndex]!;
                        var attemptWarnings = new JArray();
                        var attempt = new JObject
                        {
                            ["index"] = index,
                            ["request"] = request.DeepClone(),
                            ["template"] = templateObservation.DeepClone(),
                            ["success"] = false,
                            ["error"] = null,
                            ["warnings"] = attemptWarnings
                        };
                        attempts.Add(attempt);
                        SymbolVariant? symbol = null;
                        Observe(attempt, "symbol", () => ReadSymbol(symbol = template.SymbolVariant), attemptWarnings);
                        Observe(attempt, "symbolIsValid", () => symbol is not null && symbol.IsValid, attemptWarnings);
                        if ((bool?)attempt["symbolIsValid"] != true)
                        {
                            attempt["error"] = "The template SymbolVariant is null, invalid or unreadable; placement was skipped.";
                            continue;
                        }
                        placementIndex++;
                        try
                        {
                            // DataModel XML: PlaceAt instantiates a transient device-list template instead of copying a persistent function.
                            template.PlaceAt(page, new PointD((double)request["x"]!, (double)request["y"]!), DocumentTypeManager.DocumentType.Circuit, symbol!);
                            attempt["success"] = true;
                            JObject placed = ReadFunction(template, index, propertyScope: propertyScope);
                            attempt["placed"] = placed;
                            if ((string?)placed["name"] is { Length: > 0 } name)
                            { names.Add(name); }
                            if ((string?)placed["deviceTag"] is { Length: > 0 } deviceTag)
                            { deviceTags.Add(deviceTag); }
                        }
                        catch (Exception error) { attempt["error"] = error.ToString(); throw; }
                    }
                    if (placementIndex < requests.Count)
                    { warnings.Add("Fewer Function templates with a readable valid SymbolVariant were available than requested placements."); }
                    break;
                }
                if (!matched)
                { warnings.Add("No device-list entry matched the requested part and variant."); }
                GC.KeepAlive(item);
                // A79/A80: release cached property lists before either commit or rollback.
                propertyScope.Dispose();
                scope.Commit();
                scope.CloseUndo();
            }
            catch { scope.Rollback(); throw; }
        }
        // A79: keep only pre-commit identity strings and query fresh functions and device-list templates.
        result["readbackNames"] = new JArray(names);
        result["readbackDeviceTags"] = new JArray(deviceTags);
        var readbackWarnings = new JArray();
        result["readbackWarnings"] = readbackWarnings;
        JArray functions = ReadDeviceListFunctions(project, names, deviceTags, readbackWarnings);
        result["functions"] = functions;
        result["duplicates"] = ReadDuplicates(functions);
        using var readbackService = new DeviceService();
        ReadDeviceList(readbackService, project, result, "deviceListAfter", readbackWarnings);
    }

    private static List<(JObject Observation, StorableObject[] Templates)> ReadDeviceList(DeviceService service, Project project, JObject result, string field, JArray warnings,
        FunctionObservationScope? propertyScope = null)
    {
        var entries = new List<(JObject, StorableObject[])>();
        Observe(result, field, () =>
        {
            var observations = new JArray();
            // HEServices XML: each TemplatesInfo contains transient templates for a device-list part and variant.
            foreach (DeviceService.TemplatesInfo info in service.GetTemplatesFromDeviceList(project) ?? [])
            {
                var entryWarnings = new JArray();
                var observation = new JObject { ["warnings"] = entryWarnings };
                Observe(observation, "partNumber", () => info.PartNumber, entryWarnings);
                Observe(observation, "variant", () => info.Variant, entryWarnings);
                StorableObject[] templates = [];
                Observe(observation, "templates", () =>
                {
                    templates = info.Templates ?? [];
                    return new JArray(templates.Select((template, index) => ReadFunction(template, index, propertyScope: propertyScope)));
                }, entryWarnings);
                entries.Add((observation, templates));
                observations.Add(observation);
            }
            return observations;
        }, warnings);
        return entries;
    }

    private static JArray ReadDeviceListFunctions(Project project, HashSet<string> names, HashSet<string> deviceTags, JArray warnings)
    {
        var result = new JArray();
        if (names.Count == 0 && deviceTags.Count == 0)
        { warnings.Add("Placed templates had no readable name or device tag; function read-back is unavailable."); return result; }
        try
        {
            using var finder = new DMObjectsFinder(project);
            foreach (Function function in finder.GetFunctions(null))
            {
                FunctionPropertyList? properties = null;
                try
                {
                    var identity = new JObject();
                    Observe(identity, "name", () => function.Name, warnings);
                    Observe(identity, "deviceTag", () =>
                    {
                        properties = function.Properties;
                        using PropertyValue device = properties.FUNC_IDENTDEVICETAG;
                        return device.IsEmpty ? null : device.ToString();
                    }, warnings);
                    if (names.Contains((string?)identity["name"] ?? "") || deviceTags.Contains((string?)identity["deviceTag"] ?? ""))
                    { result.Add(ReadFunction(function, null, properties)); }
                }
                finally
                {
                    properties?.Dispose();
                    GC.KeepAlive(function);
                }
            }
        }
        catch (Exception error) { Warn(warnings, "functions", error); }
        return result;
    }

    private static StorableObject[] ReadTemplates(Function function, JObject result, string field, JArray warnings)
    {
        StorableObject[] templates = [];
        Observe(result, field, () =>
        {
            templates = function.FunctionTemplates ?? [];
            return new JArray(templates.Select((template, index) => ReadFunction(template, index)));
        }, warnings);
        GC.KeepAlive(function);
        return templates;
    }

    private static void ReadMainTemplates(Project project, string? deviceTag, JObject result, JArray warnings)
    {
        if (string.IsNullOrEmpty(deviceTag))
        { return; }
        try
        {
            using var finder = new DMObjectsFinder(project);
            Function? placed = null;
            foreach (Function function in finder.GetFunctions(null))
            {
                var identity = new JObject();
                Observe(identity, "deviceTag", () => ReadDeviceTag(function), warnings);
                if ((string?)identity["deviceTag"] != deviceTag)
                { continue; }
                Observe(identity, "isMainFunction", () => function.IsMainFunction, warnings);
                if ((bool?)identity["isMainFunction"] == true)
                {
                    // DataModel XML: previously collected template wrappers do not receive another wrapper's coverage updates.
                    ReadTemplates(function, result, "templatesAfter", warnings);
                    return;
                }
                Observe(identity, "isPlaced", () => function.IsPlaced, warnings);
                if ((bool?)identity["isPlaced"] == true)
                { placed ??= function; }
            }
            if (placed is not null)
            { ReadTemplates(placed, result, "templatesAfter", warnings); }
            else
            { warnings.Add("No fresh main or placed function was found for template read-back."); }
        }
        catch (Exception error) { Warn(warnings, "templatesAfter", error); }
    }

    private static string? ReadDeviceTag(Function function)
    {
        try
        {
            using FunctionPropertyList properties = function.Properties;
            using PropertyValue device = properties.FUNC_IDENTDEVICETAG;
            return device.IsEmpty ? null : device.ToString();
        }
        finally { GC.KeepAlive(function); }
    }

    private static JArray ReadDeviceByTag(Project project, string? deviceTag, JArray warnings)
    {
        if (!string.IsNullOrEmpty(deviceTag))
        { return ReadDevice(project, deviceTag!, warnings, deviceTagOnly: true); }
        warnings.Add("The device tag is empty or unreadable; device read-back is unavailable.");
        return new JArray();
    }

    private static void Assign(Project project, JObject input, JObject result)
    {
        string name = (string)input["name"]!;
        using (var scope = new TransactionScope("Parts assignment spike"))
        {
            try
            {
                using var finder = new DMObjectsFinder(project);
                Function function = finder.GetFunctions(null).Single(function => function.IsPlaced && function.Name == name);
                Page? page = function.Page;
                if (page is null || !AllowedPage(page.Name) || page.Name != (string)input["page"]!)
                { throw new InvalidOperationException("The assignment target must be on the explicit LargeWrite page."); }
                function.AddArticleReference((string)input["part"]!, (string?)input["variant"] ?? "", 1);
                scope.Commit();
                scope.CloseUndo();
            }
            catch { scope.Rollback(); throw; }
        }
        var warnings = new JArray();
        result["readbackWarnings"] = warnings;
        result["articleReferences"] = null;
        result["isCoveredTemplate"] = null;
        result["templatesAfter"] = new JArray();
        string? deviceTag = null;
        bool found = false;
        try
        {
            using var readback = new DMObjectsFinder(project);
            foreach (Function actual in readback.GetFunctions(null))
            {
                var identity = new JObject();
                Observe(identity, "isPlaced", () => actual.IsPlaced, warnings);
                Observe(identity, "name", () => actual.Name, warnings);
                if ((bool?)identity["isPlaced"] != true || (string?)identity["name"] != name)
                { continue; }
                found = true;
                Observe(result, "articleReferences", () => new JArray(actual.ArticleReferences.Select(article =>
                {
                    var reference = new JObject();
                    Observe(reference, "part", () => article.PartNr, warnings);
                    Observe(reference, "variant", () => article.VariantNr, warnings);
                    Observe(reference, "count", () => article.Count, warnings);
                    return reference;
                })), warnings);
                Observe(result, "isCoveredTemplate", () => actual.IsCoveredTemplate, warnings);
                Observe(identity, "deviceTag", () => ReadDeviceTag(actual), warnings);
                deviceTag = (string?)identity["deviceTag"];
                // DataModel XML: AddArticleReference assigns the part; coverage and generated templates are observations.
                ReadTemplates(actual, result, "templatesAfter", warnings);
                GC.KeepAlive(actual);
                break;
            }
        }
        catch (Exception error) { Warn(warnings, "assignedFunction", error); }
        if (!found)
        { warnings.Add("The assigned function could not be found for read-back."); }
        result["functions"] = ReadDeviceByTag(project, deviceTag, warnings);
    }

    private static JArray ReadDevice(Project project, string name, JArray warnings, bool deviceTagOnly = false)
    {
        var result = new JArray();
        try
        {
            using var finder = new DMObjectsFinder(project);
            foreach (Function function in finder.GetFunctions(null))
            {
                FunctionPropertyList? properties = null;
                try
                {
                    var identity = new JObject();
                    Observe(identity, "name", () => function.Name, warnings);
                    Observe(identity, "deviceTag", () =>
                    {
                        properties = function.Properties;
                        using PropertyValue device = properties.FUNC_IDENTDEVICETAG;
                        return device.IsEmpty ? null : device.ToString();
                    }, warnings);
                    if ((string?)identity["deviceTag"] == name || !deviceTagOnly && (string?)identity["name"] == name)
                    { result.Add(ReadFunction(function, null, properties)); }
                }
                catch (Exception error) { Warn(warnings, "deviceMembership", error); }
                finally
                {
                    properties?.Dispose();
                    GC.KeepAlive(function);
                }
            }
        }
        catch (Exception error) { Warn(warnings, "functions", error); }
        return result;
    }

    private static JObject ReadFunction(object? item, int? index, FunctionPropertyList? borrowedProperties = null, FunctionObservationScope? propertyScope = null)
    {
        var warnings = new JArray();
        var result = new JObject { ["index"] = index, ["type"] = item?.GetType().FullName, ["warnings"] = warnings };
        if (item is IFunctionBase functionBase)
        { Observe(result, "name", () => functionBase.Name, warnings); }
        if (item is not Function function)
        {
            if (item is Connection connection)
            {
                // DataModel XML: FunctionTemplates may also contain transient Connection objects.
                Observe(result, "isTemplate", () => connection.IsTemplate, warnings);
                Observe(result, "isCoveredTemplate", () => connection.IsCoveredTemplate, warnings);
            }
            warnings.Add("The returned object has no two-dimensional Function members.");
            GC.KeepAlive(item);
            return result;
        }
        Observe(result, "category", () => function.FunctionCategory.ToString(), warnings);
        Observe(result, "isMainFunction", () => function.IsMainFunction, warnings);
        Observe(result, "isPlaced", () => function.IsPlaced, warnings);
        Observe(result, "isTemplate", () => function.IsTemplate, warnings);
        Observe(result, "isCoveredTemplate", () => function.IsCoveredTemplate, warnings);
        Observe(result, "page", () => function.Page?.Name, warnings);
        Observe(result, "location", () => new JArray(function.Location.X, function.Location.Y), warnings);
        Observe(result, "symbol", () => ReadSymbol(function.SymbolVariant), warnings);
        Observe(result, "pins", () => new JArray(function.Pins.Select((pin, pinIndex) => ReadPin(pin, pinIndex))), warnings);
        FunctionPropertyList? properties = null;
        try
        {
            Observe(result, "properties", () =>
            {
                properties = borrowedProperties ?? propertyScope?.GetProperties(function) ?? function.Properties;
                Observe(result, "deviceTag", () =>
                {
                    using PropertyValue device = properties.FUNC_IDENTDEVICETAG;
                    return device.IsEmpty ? null : device.ToString();
                }, warnings);
                return ReadProperties(properties, warnings);
            }, warnings);
        }
        finally { if (borrowedProperties is null && propertyScope is null) { properties?.Dispose(); } }
        Observe(result, "displayedProperties", () => function.Page is { } page
            ? new JArray(page.AllPlacements.OfType<PropertyPlacement>().Where(text => text.Parent?.ObjectIdentifier == function.ObjectIdentifier).Select(ReadDisplay)) : new JArray(), warnings);
        GC.KeepAlive(function);
        return result;
    }

    private static JObject? ReadSymbol(SymbolVariant? variant)
    {
        if (variant is null || !variant.IsValid)
        { return null; }
        var warnings = new JArray();
        var result = new JObject { ["warnings"] = warnings };
        Observe(result, "library", () => variant.SymbolLibraryName, warnings);
        Observe(result, "name", () => variant.SymbolName, warnings);
        Observe(result, "variant", () => variant.VariantNr, warnings);
        Observe(result, "number", () =>
        {
            Symbol symbol = variant.Parent;
            try
            {
                using SymbolPropertyList properties = symbol.Properties;
                using PropertyValue number = properties.SYMB_NR;
                return number.IsEmpty ? null : number.ToString();
            }
            finally { GC.KeepAlive(symbol); }
        }, warnings);
        return result;
    }

    private static JObject ReadPin(Pin pin, int index)
    {
        var warnings = new JArray();
        var result = new JObject { ["arrayIndex"] = index, ["warnings"] = warnings };
        Observe(result, "index", () => pin.Index, warnings);
        Observe(result, "name", () => pin.Name, warnings);
        Observe(result, "designation", () => pin.Designation, warnings);
        return result;
    }

    private static JObject ReadProperties(FunctionPropertyList properties, JArray warnings)
    {
        var result = new JObject();
        foreach (int id in _propertyIds)
        {
            string key = id.ToString(CultureInfo.InvariantCulture);
            try
            {
                using PropertyValue value = properties[(Eplan.EplApi.DataModel.Properties.Function)id];
                using PropertyDefinition definition = value.Definition;
                if (definition.IsIndexed)
                {
                    var indexes = new JObject();
                    foreach (int index in value.Indexes)
                    {
                        try
                        {
                            using PropertyValue item = value[index];
                            if (!item.IsEmpty && item.ToString() is { Length: > 0 } text)
                            { indexes[index.ToString(CultureInfo.InvariantCulture)] = text; }
                        }
                        catch (Exception error) { Warn(warnings, key + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", error); }
                    }
                    if (indexes.HasValues)
                    { result[key] = indexes; }
                }
                else if (!value.IsEmpty && value.ToString() is { Length: > 0 } text)
                { result[key] = text; }
            }
            catch (Exception error) { Warn(warnings, key, error); }
        }
        return result;
    }

    private static JObject ReadNameProperties(FunctionPropertyList properties, JArray warnings)
    {
        var result = new JObject();
        AnyPropertyId[] ids = properties.ExistingIds;
        try
        {
            foreach (AnyPropertyId id in ids)
            {
                try
                {
                    using PropertyValue value = properties[id];
                    using PropertyDefinition definition = value.Definition;
                    string key = id.AsString;
                    if (definition.IsIndexed)
                    {
                        var indexes = new JObject();
                        foreach (int index in value.Indexes)
                        {
                            using PropertyValue item = value[index];
                            if (!item.IsEmpty && item.ToString() is { Length: > 0 } text)
                            { indexes[index.ToString(CultureInfo.InvariantCulture)] = text; }
                        }
                        if (indexes.HasValues)
                        { result[key] = indexes; }
                    }
                    else if (!value.IsEmpty && value.ToString() is { Length: > 0 } text)
                    { result[key] = text; }
                }
                catch (Exception error) { Warn(warnings, "nameProperty", error); }
            }
        }
        finally { foreach (AnyPropertyId id in ids) { id.Dispose(); } }
        return result;
    }

    private static JObject ReadDisplay(PropertyPlacement placement)
    {
        var warnings = new JArray();
        var result = new JObject { ["warnings"] = warnings };
        try
        {
            using AnyPropertyId property = placement.DisplayedProperty;
            result["id"] = property.AsInt;
            result["index"] = property.Index;
        }
        catch (Exception error) { Warn(warnings, "property", error); }
        Observe(result, "text", () => placement.GetDisplayString(), warnings);
        return result;
    }

    private static void Observe(JObject result, string field, Func<object?> read, JArray warnings)
    {
        try
        { object? value = read(); result[field] = value is null ? null : JToken.FromObject(value); }
        catch (Exception error) { result[field] = null; Warn(warnings, field, error); }
    }

    private sealed class FunctionObservationScope : IDisposable
    {
        private readonly List<(Function Function, FunctionPropertyList Properties)> _properties = [];

        public FunctionPropertyList GetProperties(Function function)
        {
            foreach ((Function owner, FunctionPropertyList properties) in _properties)
            {
                if (ReferenceEquals(owner, function))
                { return properties; }
            }
            FunctionPropertyList result = function.Properties;
            _properties.Add((function, result));
            return result;
        }

        public void Dispose()
        {
            foreach ((Function owner, FunctionPropertyList properties) in _properties)
            {
                try
                { properties.Dispose(); }
                finally { GC.KeepAlive(owner); }
            }
            _properties.Clear();
        }
    }

    private static void Warn(JArray warnings, string field, Exception error) => warnings.Add(new JObject { ["field"] = field, ["error"] = error.ToString() });
}
