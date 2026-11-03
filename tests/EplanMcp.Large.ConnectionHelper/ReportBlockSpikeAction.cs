using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Test-only T.4 observations of native report settings and scratch report creation.</summary>
public sealed class ReportBlockSpikeAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeReportBlockSpike"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var warnings = new JArray();
        var result = new JObject { ["success"] = false, ["warnings"] = warnings };
        try
        {
            using var locking = new LockingStep();
            MainThread.Use(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            JObject input = JObject.Parse(File.ReadAllText(file));
            Project project = new SelectionSet().GetCurrentProject(false);
            if (!string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("The explicitly requested project must be current."); }
            result["mode"] = input["mode"]?.DeepClone();
            result["project"] = project.ProjectFullName;
            if ((string?)input["mode"] == "read")
            {
                string[] names = input["pages"]!.Values<string>().Select(name => name!).ToArray();
                result["pages"] = new JArray(names.Select(name => ReadPage(project, name, warnings)));
            }
            else if ((string?)input["mode"] == "create")
            {
                string expected = Path.Combine(DataRoot.Path, "scratch", "writes", "LargeWrite");
                if (!string.Equals(project.ProjectFullName, expected, StringComparison.OrdinalIgnoreCase))
                { throw new InvalidOperationException("Report creation is restricted to the current scratch LargeWrite copy."); }
                string firstPage = (string)input["firstPageName"]!;
                if (!(firstPage.StartsWith("&AAB1/", StringComparison.Ordinal) || firstPage.StartsWith("+A1&EMA2/", StringComparison.Ordinal))
                    || project.Pages.Any(page => string.Equals(page.Name, firstPage, StringComparison.Ordinal)))
                { throw new InvalidOperationException("Supply a new table-of-contents or terminal-diagram first page."); }
                var settings = (JObject)input["settings"]!;
                string[] before = project.Pages.Select(page => page.Name).ToArray();
                Create(project, input, settings, result, warnings);
                // A79: only managed names/settings survive the transaction; reacquire project, pages and blocks.
                Project readback = new SelectionSet().GetCurrentProject(false);
                if (!string.Equals(readback.ProjectFullName, expected, StringComparison.OrdinalIgnoreCase))
                { throw new InvalidOperationException("The current project changed before report readback."); }
                result["readbackScope"] = "New pages, requested first page, and existing reports with the same Type/FormName (device reports may overwrite).";
                var pages = new JArray();
                foreach (Page page in readback.Pages)
                {
                    JToken name = Observe("readback.name", () => page.Name, warnings);
                    if (name.Type != JTokenType.String)
                    { continue; }
                    string pageName = (string)name!;
                    bool existed = before.Contains(pageName, StringComparer.Ordinal);
                    bool include = !existed || string.Equals(pageName, firstPage, StringComparison.Ordinal);
                    if (!include)
                    {
                        JToken candidate = Observe(pageName + ".candidate", () =>
                        {
                            using ReportBlock? block = page.ReportBlock;
                            if (block is null)
                            { return false; }
                            JToken type = Observe(pageName + ".candidate.type", () => (int)block.Type, warnings);
                            JToken form = Observe(pageName + ".candidate.formName", () => block.FormName, warnings);
                            return type.Type == JTokenType.Integer && (int)type == (int)settings["type"]!
                                && form.Type == JTokenType.String && string.Equals((string?)form, (string?)settings["formName"], StringComparison.Ordinal);
                        }, warnings);
                        include = candidate.Type == JTokenType.Boolean && (bool)candidate;
                    }
                    if (include)
                    {
                        JObject row = ReadPage(readback, pageName, warnings);
                        row["existedBefore"] = existed;
                        pages.Add(row);
                    }
                }
                result["pages"] = pages;
                result["removedPageNames"] = new JArray(before.Except(readback.Pages.Select(page => page.Name), StringComparer.Ordinal));
                if (!pages.OfType<JObject>().Any(page => string.Equals((string?)page["requestedName"], firstPage, StringComparison.Ordinal)))
                { warnings.Add(new JObject { ["field"] = "firstPageName", ["warning"] = "The requested first page was not found after the transaction." }); }
            }
            else
            { throw new InvalidOperationException("Mode must be read or create."); }
            result["success"] = true;
        }
        catch (Exception error) { result["error"] = Error(error); }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static void Create(Project project, JObject input, JObject settings, JObject result, JArray warnings)
    {
        var creation = new JObject { ["state"] = "notStarted", ["firstPageName"] = input["firstPageName"]!.DeepClone() };
        result["creation"] = creation;
        var parts = new List<FunctionBasePropertyList>();
        using var scope = new TransactionScope("Report block T.4 spike");
        try
        {
            // DataModelu.xml: ReportBlock.#ctor + Create(Project) create a transient settings object.
            using (var block = new ReportBlock())
            {
                block.Create(project);
                block.Type = (DocumentTypeManager.DocumentType)(int)settings["type"]!;
                SetString(settings, "formName", value => block.FormName = value, warnings);
                SetString(settings, "filterSchemaName", value => block.FilterSchemaName = value, warnings);
                SetString(settings, "filterSchemaData", value => block.FilterSchemaData = value, warnings);
                SetString(settings, "sortSchemaName", value => block.SortSchemaName = value, warnings);
                SetString(settings, "sortSchemaData", value => block.SortSchemaData = value, warnings);
                SetString(settings, "functionFilterSchemaName", value => block.FunctionFilterSchemaName = value, warnings);
                SetString(settings, "functionFilterSchemaData", value => block.FunctionFilterSchemaData = value, warnings);
                SetString(settings, "functionSortSchemaName", value => block.FunctionSortSchemaName = value, warnings);
                SetString(settings, "functionSortSchemaData", value => block.FunctionSortSchemaData = value, warnings);
                SetString(settings, "action", value => block.Action = value, warnings);
                if (settings["manualPageDescription"]?.Type == JTokenType.Null)
                { block.ManualPageDescription = null!; }
                else
                {
                    SetString(settings, "manualPageDescription", value =>
                    {
                        using var description = new MultiLangString();
                        description.SetAsString(value);
                        block.ManualPageDescription = description;
                    }, warnings);
                }
                // XML: a nonempty ManualPageDescription sets this flag to false, so restore the flag last.
                if (settings["isAutomaticPageDescription"]?.Type == JTokenType.Boolean)
                { block.IsAutomaticPageDescription = (bool)settings["isAutomaticPageDescription"]!; }
                else
                { warnings.Add(new JObject { ["field"] = "isAutomaticPageDescription", ["warning"] = "Source value was unreadable; the native default was retained." }); }
                if ((bool?)input["limitToTerminalStrip"] == true)
                {
                    PrepareTerminalStrip(project, settings, creation, warnings, parts);
                    if (parts.Count > 0)
                    { block.DeviceTagNameParts = parts.ToArray(); }
                }
                creation["appliedSettings"] = ReadBlock(block, "appliedSettings", warnings);
                // HEServicesu.xml: CreateReport(ReportBlock,string) can replace an existing device report.
                using var reports = new Reports();
                reports.CreateReport(block, (string)input["firstPageName"]!);
            }
            foreach (FunctionBasePropertyList part in parts)
            { part.Dispose(); }
            parts.Clear();
            scope.Commit();
            scope.CloseUndo();
            creation["state"] = "committed";
        }
        catch (Exception error)
        {
            creation["exception"] = Error(error);
            // Release transient property lists before rollback; no native wrapper crosses the aborted scope (A79).
            foreach (FunctionBasePropertyList part in parts)
            { part.Dispose(); }
            parts.Clear();
            try
            { scope.Rollback(); creation["state"] = "rolledBack"; }
            catch (Exception rollbackError) { creation["state"] = "rollbackFailed"; creation["rollbackException"] = Error(rollbackError); }
        }
    }

    private static void PrepareTerminalStrip(Project project, JObject settings, JObject creation, JArray warnings, List<FunctionBasePropertyList> parts)
    {
        // DataModelu.xml: the parameterless FunctionBasePropertyList is offline; the FunctionBase ctor is internal-only.
        try
        {
            var source = settings["deviceTagNameParts"] as JArray;
            if (source is null || source.Count == 0)
            { throw new InvalidOperationException("No readable source DeviceTagNameParts property IDs were available."); }
            using var finder = new DMObjectsFinder(project);
            TerminalStrip strip = finder.GetTerminalStrips(null).OrderBy(item => item.Name, StringComparer.Ordinal).First();
            creation["terminalStrip"] = strip.Name;
            try
            {
                using FunctionBasePropertyList name = strip.NameParts;
                foreach (JObject sourceList in source.OfType<JObject>())
                {
                    var target = new FunctionBasePropertyList();
                    parts.Add(target);
                    var properties = sourceList["properties"] as JArray
                        ?? throw new InvalidOperationException("A source name-parts list could not be read.");
                    if (properties.Count == 0)
                    { throw new InvalidOperationException("A source name-parts list has no readable property IDs."); }
                    foreach (JObject property in properties.OfType<JObject>())
                    {
                        if (property["id"]?.Type != JTokenType.Integer || property["index"]?.Type != JTokenType.Integer)
                        { throw new InvalidOperationException("A source name-part property ID/index could not be read."); }
                        using AnyPropertyId id = (int)property["id"]!;
                        int index = (int)property["index"]!;
                        using PropertyValue value = index == 0 ? name[id] : name[id, index];
                        if (value.IsEmpty)
                        { throw new InvalidOperationException("The target strip has no value for source property " + id.AsString + "."); }
                        if (index == 0)
                        { target[id] = value; }
                        else
                        { target[id, index] = value; }
                    }
                }
                creation["deviceTagNameParts"] = ReadNameParts(parts.ToArray(), "terminalStrip.nameParts", warnings);
                creation["deviceRestriction"] = "Existing LargeWrite terminal strip, using the source report's property IDs and indexes.";
            }
            finally { GC.KeepAlive(strip); }
        }
        catch (Exception error)
        {
            // The brief explicitly permits the whole-project probe when the restriction cannot be constructed.
            creation["deviceRestriction"] = "Whole-project terminal report: the device restriction could not be constructed.";
            warnings.Add(new JObject { ["field"] = "deviceTagNameParts", ["warning"] = error.Message, ["exception"] = Error(error) });
            foreach (FunctionBasePropertyList part in parts)
            { part.Dispose(); }
            parts.Clear();
        }
    }

    private static JObject ReadPage(Project project, string name, JArray warnings)
    {
        var row = new JObject { ["requestedName"] = name };
        JToken observation = Observe(name, () =>
        {
            Page page = project.Pages.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            try
            {
                row["name"] = Observe(name + ".name", () => page.Name, warnings);
                row["pageType"] = Observe(name + ".pageType", () => page.PageType.ToString(), warnings);
                row["description"] = Observe(name + ".description", () =>
                {
                    using PagePropertyList properties = page.Properties;
                    using PropertyValue value = properties.PAGE_NOMINATIOMN;
                    if (value.IsEmpty)
                    { return null; }
                    using MultiLangString text = value.ToMultiLangString();
                    return text.GetAsString();
                }, warnings);
                row["reportBlock"] = Observe(name + ".reportBlock", () =>
                {
                    using ReportBlock? block = page.ReportBlock;
                    if (block is null)
                    { throw new InvalidOperationException("Page.ReportBlock returned null."); }
                    return ReadBlock(block, name + ".reportBlock", warnings);
                }, warnings);
                row["objectKinds"] = Observe(name + ".objectKinds", () => new JObject(page.AllPlacements
                    .GroupBy(item => item.GetType().FullName ?? item.GetType().Name, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new JProperty(group.Key, group.Count()))), warnings);
                return true;
            }
            finally { GC.KeepAlive(page); }
        }, warnings);
        if (observation is JObject)
        {
            row["reportBlock"] = observation;
            row["name"] = observation.DeepClone();
            row["pageType"] = observation.DeepClone();
            row["description"] = observation.DeepClone();
        }
        return row;
    }

    private static JObject ReadBlock(ReportBlock block, string path, JArray warnings)
    {
        try
        {
            return new JObject
            {
                ["type"] = Observe(path + ".type", () => (int)block.Type, warnings),
                ["typeName"] = Observe(path + ".typeName", () => block.Type.ToString(), warnings),
                ["formName"] = Observe(path + ".formName", () => block.FormName, warnings),
                ["filterSchemaName"] = Observe(path + ".filterSchemaName", () => block.FilterSchemaName, warnings),
                ["filterSchemaData"] = Observe(path + ".filterSchemaData", () => block.FilterSchemaData, warnings),
                ["sortSchemaName"] = Observe(path + ".sortSchemaName", () => block.SortSchemaName, warnings),
                ["sortSchemaData"] = Observe(path + ".sortSchemaData", () => block.SortSchemaData, warnings),
                ["functionFilterSchemaName"] = Observe(path + ".functionFilterSchemaName", () => block.FunctionFilterSchemaName, warnings),
                ["functionFilterSchemaData"] = Observe(path + ".functionFilterSchemaData", () => block.FunctionFilterSchemaData, warnings),
                ["functionSortSchemaName"] = Observe(path + ".functionSortSchemaName", () => block.FunctionSortSchemaName, warnings),
                ["functionSortSchemaData"] = Observe(path + ".functionSortSchemaData", () => block.FunctionSortSchemaData, warnings),
                ["deviceTag"] = Observe(path + ".deviceTag", () => block.DeviceTag, warnings),
                ["deviceTagNameParts"] = Observe(path + ".deviceTagNameParts", () =>
                {
                    FunctionBasePropertyList[] parts = block.DeviceTagNameParts;
                    try
                    { return ReadNameParts(parts, path + ".deviceTagNameParts", warnings); }
                    finally { foreach (FunctionBasePropertyList part in parts) { part.Dispose(); } }
                }, warnings),
                ["isAutomaticPageDescription"] = Observe(path + ".isAutomaticPageDescription", () => block.IsAutomaticPageDescription, warnings),
                ["manualPageDescription"] = Observe(path + ".manualPageDescription", () =>
                {
                    using MultiLangString description = block.ManualPageDescription;
                    return description?.GetAsString();
                }, warnings),
                ["action"] = Observe(path + ".action", () => block.Action, warnings),
            };
        }
        finally { GC.KeepAlive(block); }
    }

    private static JArray ReadNameParts(FunctionBasePropertyList[] parts, string path, JArray warnings)
    {
        var lists = new JArray();
        for (int listIndex = 0; listIndex < parts.Length; listIndex++)
        {
            FunctionBasePropertyList part = parts[listIndex];
            string listPath = path + "[" + listIndex.ToString(CultureInfo.InvariantCulture) + "]";
            lists.Add(new JObject
            {
                ["properties"] = Observe(listPath, () =>
            {
                AnyPropertyId[] ids = part.ExistingIds;
                var properties = new JArray();
                try
                {
                    foreach (AnyPropertyId id in ids)
                    {
                        JToken number = Observe(listPath + ".id", () => id.AsInt, warnings);
                        JToken indexed = Observe(listPath + ".isIndexed", () =>
                        { using PropertyDefinition definition = id.Definition; return definition.IsIndexed; }, warnings);
                        if (indexed.Type != JTokenType.Boolean)
                        {
                            properties.Add(new JObject { ["id"] = number.DeepClone(), ["isIndexed"] = indexed.DeepClone(), ["index"] = indexed.DeepClone() });
                            continue;
                        }
                        JToken indexes = indexed.Type == JTokenType.Boolean && (bool)indexed
                            ? Observe(listPath + ".indexes", () => { using PropertyValue value = part[id]; return value.Indexes; }, warnings)
                            : new JArray(0);
                        if (indexes is not JArray array)
                        { properties.Add(new JObject { ["id"] = number.DeepClone(), ["index"] = indexes }); continue; }
                        foreach (int index in array.Values<int>())
                        {
                            string valuePath = listPath + "." + number.ToString() + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                            properties.Add(new JObject
                            {
                                ["id"] = number.DeepClone(),
                                ["index"] = index,
                                ["isIndexed"] = indexed.DeepClone(),
                                ["isEmpty"] = Observe(valuePath + ".isEmpty", () =>
                                { using PropertyValue value = index == 0 ? part[id] : part[id, index]; return value.IsEmpty; }, warnings),
                                ["value"] = Observe(valuePath + ".value", () =>
                                { using PropertyValue value = index == 0 ? part[id] : part[id, index]; return value.IsEmpty ? null : value.ToString(); }, warnings),
                            });
                        }
                    }
                }
                finally { foreach (AnyPropertyId id in ids) { id.Dispose(); } GC.KeepAlive(part); }
                return properties;
            }, warnings)
            });
        }
        return lists;
    }

    private static void SetString(JObject settings, string field, Action<string> set, JArray warnings)
    {
        if (settings[field]?.Type == JTokenType.String)
        { set((string)settings[field]!); }
        else if (settings[field]?.Type == JTokenType.Null)
        { set(null!); }
        else
        { warnings.Add(new JObject { ["field"] = field, ["warning"] = "Source value was unreadable; the native default was retained." }); }
    }

    private static JObject Error(Exception error) => new()
    { ["type"] = error.GetType().FullName, ["message"] = error.Message, ["details"] = error.ToString() };

    private static JToken Observe(string field, Func<object?> read, JArray warnings)
    {
        try
        {
            object? value = read();
            return value is null ? JValue.CreateNull() : value is JToken token ? token : JToken.FromObject(value);
        }
        catch (Exception error)
        {
            var warning = new JObject { ["field"] = field, ["warning"] = error.Message, ["exception"] = Error(error) };
            warnings.Add(warning);
            return warning.DeepClone();
        }
    }
}
