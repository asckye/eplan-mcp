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
        using var readback = new DMObjectsFinder(project);
        Function actual = readback.GetFunctions(null).Single(function => function.IsPlaced && function.Name == name);
        result["articleReferences"] = new JArray(actual.ArticleReferences.Select(article => new JObject { ["part"] = article.PartNr, ["variant"] = article.VariantNr, ["count"] = article.Count }));
    }

    private static JArray ReadDevice(Project project, string name, JArray warnings)
    {
        var result = new JArray();
        using var finder = new DMObjectsFinder(project);
        foreach (Function function in finder.GetFunctions(null))
        {
            try
            {
                using FunctionPropertyList properties = function.Properties;
                using PropertyValue device = properties.FUNC_IDENTDEVICETAG;
                if (function.Name == name || !device.IsEmpty && device.ToString() == name)
                { result.Add(ReadFunction(function, null, properties)); }
            }
            catch (Exception error) { Warn(warnings, "deviceMembership", error); }
            finally { GC.KeepAlive(function); }
        }
        return result;
    }

    private static JObject ReadFunction(IFunctionBase item, int? index, FunctionPropertyList? borrowedProperties = null)
    {
        var warnings = new JArray();
        var result = new JObject { ["index"] = index, ["type"] = item.GetType().FullName, ["warnings"] = warnings };
        Observe(result, "name", () => item.Name, warnings);
        if (item is not Function function)
        {
            warnings.Add("The returned object has no two-dimensional Function members.");
            return result;
        }
        Observe(result, "category", () => function.FunctionCategory.ToString(), warnings);
        Observe(result, "isMainFunction", () => function.IsMainFunction, warnings);
        Observe(result, "isPlaced", () => function.IsPlaced, warnings);
        Observe(result, "page", () => function.Page?.Name, warnings);
        Observe(result, "location", () => new JArray(function.Location.X, function.Location.Y), warnings);
        Observe(result, "symbol", () => ReadSymbol(function.SymbolVariant), warnings);
        Observe(result, "pins", () => new JArray(function.Pins.Select((pin, pinIndex) => ReadPin(pin, pinIndex))), warnings);
        try
        {
            FunctionPropertyList properties = borrowedProperties ?? function.Properties;
            try
            { result["properties"] = ReadProperties(properties, warnings); }
            finally { if (borrowedProperties is null) { properties.Dispose(); } }
        }
        catch (Exception error) { Warn(warnings, "properties", error); }
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

    private static void Warn(JArray warnings, string field, Exception error) => warnings.Add(new JObject { ["field"] = field, ["error"] = error.ToString() });
}
