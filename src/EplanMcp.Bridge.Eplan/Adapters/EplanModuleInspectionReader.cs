using System.Diagnostics;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

#pragma warning disable CA2249 // Linked tests target .NET 10; .NET Framework lacks Contains(StringComparison).
/// <summary>S36/A33/A51/A56 discovery only; cached lists are owned, all StorableObjects and pins are borrowed (A39).</summary>
internal sealed class EplanModuleInspectionReader : IModuleInspectionReader
{
    private static readonly int[] _unindexed = [0];
    public ModuleInspectData Read(ModuleInspectArgs args)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(args.Project);
        if (!project.IsReadOnly)
        { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Inspect only a reference copy opened read-only."); }
        using var finder = new DMObjectsFinder(project);
        var watch = Stopwatch.StartNew();
        var result = new ModuleInspectData { Coverage = "functions pages connections crossPageReferences placeholderBindings properties".Split(' ').ToDictionary(k => k, _ => true, StringComparer.Ordinal) };
        var lists = new Dictionary<string, UniversalPropertyList>(StringComparer.Ordinal);
        var rows = new Dictionary<string, JObject>(StringComparer.Ordinal);
        var queue = new Queue<StorableObject>();
        int pageCount = 0;
        void Check()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed.TotalSeconds >= 10 || pageCount > 50 || rows.Count > 10000 || result.References.Count > 10000 || result.Placeholders.Count > 200 || result.Gaps.Count > 10000)
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Module discovery exceeded its evidence budget; select a smaller scope without clipping relationships."); }
        }
        void Gap(string coverage, string pointer, string message)
        { result.Coverage[coverage] = false; result.Gaps.Add(new { pointer, message }); Check(); }
        void Read(string coverage, string pointer, Action action)
        {
            Check();
            try
            { action(); }
            catch (Exception ex) when (ex is not (System.OperationCanceledException or BridgeErrorException))
            { Gap(coverage, pointer, "Native discovery failed here; ask the engineer to check this relationship or property."); }
        }
        static string Key(StorableObject obj) => string.Join("/", obj.ToStringIdentifier().Split('/').Skip(1).Take(2));
        string Add(StorableObject obj)
        {
            Check();
            string key = obj.DatabaseIdentifier == project.DatabaseIdentifier ? Key(obj)
                : throw new InvalidOperationException("Foreign project endpoint.");
            if (rows.ContainsKey(key))
            { return key; }
            Page? page = obj is Connection c ? c.Page : (obj as Placement)?.Page;
            string name = obj switch { Function f => f.Name, Page p => p.Name, Project p => p.ProjectName, PlaceHolder h => h.Name, _ => key };
            var row = new JObject { ["id"] = key, ["kind"] = obj switch { Function => "function", Page => "page", Project => "project", Connection => "connection", PlaceHolder => "placeholder", _ => "graphic" }, ["name"] = string.IsNullOrWhiteSpace(name) ? key : name, ["page"] = page?.Name, ["properties"] = new JArray() };
            rows.Add(key, row);
            result.Objects.Add(row);
            queue.Enqueue(obj);
            pageCount += obj is Page ? 1 : 0;
            if (page is not null)
            { Edge(obj, page, "page"); }
            return key;
        }
        void Edge(StorableObject from, StorableObject to, string kind) => result.References.Add(new { from = Add(from), to = Add(to), kind });
        void Edges(StorableObject from, IEnumerable<StorableObject> peers, string kind)
        { foreach (StorableObject peer in peers) { Edge(from, peer, kind); } }
        UniversalPropertyList List(StorableObject obj) => lists.TryGetValue(Key(obj), out UniversalPropertyList? list) ? list
            : lists[Key(obj)] = obj switch { Function f => f.Properties, Page p => p.Properties, Project p => p.Properties, PlaceHolder h => h.Properties, _ => obj.Properties };
        JArray Texts(Func<MultiLangString?> open)
        {
            using MultiLangString? text = open();
            if (text is null)
            { return []; }
            var languages = new LanguageList();
            int count = text.GetLanguageList(ref languages);
            return new JArray(Enumerable.Range(0, count <= 200 ? count : throw new InvalidOperationException("Too many languages.")).Select(i =>
            { Check(); using var code = new ISOCode(languages.get_Language(i)); return new JObject { ["language"] = code.GetString(), ["value"] = text.GetString(languages.get_Language(i)) }; }));
        }
        JToken[] Actual(StorableObject obj, int number, int index)
        {
            var properties = (JArray)rows[Add(obj)]["properties"]!;
            var read = new JArray();
            Read("properties", "/objects/" + result.Objects.IndexOf(rows[Key(obj)]) + "/properties", () =>
            {
                using AnyPropertyId id = number;
                using PropertyValue value = index == 0 ? List(obj)[id] : List(obj)[id, index];
                using PropertyDefinition definition = value.Definition;
                if (value.IsEmpty)
                { read.Add(new JObject { ["language"] = null, ["state"] = "empty" }); }
                else if (definition.Type == PropertyDefinition.PropertyType.MultilangString)
                { foreach (JObject item in Texts(value.ToMultiLangString)) { item["state"] = "value"; read.Add(item); } }
                else
                { read.Add(new JObject { ["language"] = null, ["state"] = "value", ["value"] = definition.Type == PropertyDefinition.PropertyType.Bool ? new JValue(value.ToBool()) : new JValue(value.ToString()) }); }
            });
            if (read.Count == 0)
            { read.Add(new JObject { ["language"] = null, ["state"] = "unavailable" }); Gap("properties", "/objects", "A requested property has no readable stored value."); }
            foreach (JObject item in read)
            { item["propertyId"] = number; item["index"] = index; properties.Add(item); }
            return properties.Where(p => (int)p["propertyId"]! == number && (int)p["index"]! == index).ToArray();
        }
        try
        {
            Page[] pages = [];
            Function[] functions = [];
            Read("pages", "/coverage/pages", () => pages = finder.GetPages(null) ?? throw new InvalidOperationException("Missing pages."));
            Read("functions", "/coverage/functions", () => functions = EplanPropertyReadAdapter.Functions(finder, Check).Concat(pages.SelectMany(p => p.Functions)).GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()).ToArray());
            foreach (string missing in args.Pages.Except(pages.Select(p => p.Name), StringComparer.Ordinal))
            { Gap("pages", "/objects", "A requested page was not found in the reference copy: " + missing); }
            foreach (Page page in pages.Where(p => args.Pages.Contains(p.Name, StringComparer.Ordinal)))
            {
                Read("functions", "/objects", () => { Add(page); foreach (Function f in functions.Where(f => page.Equals(f.Page))) { Add(f); } });
                Read("placeholderBindings", "/placeholders", () => { foreach (PlaceHolder h in page.AllPlacements.OfType<PlaceHolder>()) { Add(h); } });
            }
            Read("connections", "/references", () =>
            {
                Connection[] connections = finder.GetConnections(null);
                if (connections.Any(c => c is global::Eplan.EplApi.DataModel.E3D.Connection3D))
                { Gap("connections", "/references", "3D connection scope and endpoints are not supported."); }
                foreach (Connection c in connections.Where(c => c is not global::Eplan.EplApi.DataModel.E3D.Connection3D && args.Pages.Contains(c.Page?.Name, StringComparer.Ordinal)))
                { Add(c); }
            });
            while (queue.Count > 0)
            {
                StorableObject obj = queue.Dequeue();
                string pointer = "/objects/" + result.Objects.IndexOf(rows[Key(obj)]);
                if (obj is Page or Project)
                { continue; }
                Read("crossPageReferences", pointer, () => Edges(obj, obj.CrossReferencedObjectsAll ?? throw new InvalidOperationException("Missing cross references."), "cross_reference"));
                if (obj is Function function)
                {
                    Read("functions", pointer, () => Edges(obj, function.AllSubFunctions ?? throw new InvalidOperationException("Missing subfunctions."), "subfunction"));
                    Read("functions", pointer, () => Edges(obj, function.ParentFunction is { } parent ? new[] { parent } : [], "parent"));
                    Read("functions", pointer, () => Edges(obj, functions.Where(f => !string.IsNullOrWhiteSpace(function.Name) && f.Name == function.Name && Key(f) != Key(obj)), "same_name"));
                    Read("connections", pointer, () => { using var filter = new ConnectionsFilter { Function = function }; foreach (Connection c in finder.GetConnections(filter)) { Add(c); } });
                }
                if (obj is Connection connection)
                { Read("connections", pointer, () => { foreach (Pin? pin in new[] { connection.StartPin, connection.EndPin }) { if (pin?.ParentFunction is { } target) { Edge(obj, target, "connection"); } else { Gap("connections", pointer, "A connection endpoint cannot be resolved."); } } }); }
                if (obj is not PlaceHolder holder)
                { continue; }
                Read("placeholderBindings", "/placeholders", () =>
                {
                    string[] variables = holder.VariableNames.Cast<string>().ToArray();
                    if (variables.Length > 200 || holder.NumberOfRecords > 200)
                    { throw new InvalidOperationException("Placeholder exceeds limits."); }
                    var sets = new JArray();
                    var bindings = new JArray();
                    result.Placeholders.Add(new JObject { ["object"] = Add(holder), ["name"] = holder.Name, ["variables"] = new JArray(variables), ["valueSets"] = sets, ["bindings"] = bindings });
                    foreach (string set in Enumerable.Range(0, holder.NumberOfRecords).Select(holder.get_NameOfRecord))
                    {
                        var values = new JObject();
                        sets.Add(new JObject { ["name"] = set, ["values"] = values });
                        foreach (string variable in variables)
                        { Read("placeholderBindings", "/placeholders", () => values[variable] = Texts(() => holder.GetValue(set, variable))); }
                    }
                    Gap("placeholderBindings", "/placeholders", "The complete directory of unset/graphic property bindings is unproven; discovered entries are partial evidence.");
                    foreach (StorableObject target in holder.AssignedObjects.Concat(new StorableObject?[] { holder.Page, project }).OfType<StorableObject>().GroupBy(Key, StringComparer.Ordinal).Select(g => g.First()))
                    {
                        Read("properties", "/placeholders", () =>
                        {
                            Add(target);
                            AnyPropertyId[] ids = List(target).ExistingIds;
                            try
                            {
                                foreach (AnyPropertyId id in ids)
                                {
                                    Read("properties", "/placeholders", () =>
                                    {
                                        using PropertyDefinition definition = id.Definition;
                                        using AnyPropertyId number = id.AsInt;
                                        using PropertyValue parent = List(target)[number];
                                        foreach (int index in definition.IsIndexed ? parent.Indexes : _unindexed)
                                        {
                                            using AnyPropertyId descriptor = id.AsInt;
                                            descriptor.Index = checked((short)index);
                                            JArray expressions = Texts(() => target is Project ? holder.GetProjectPropertyEntry(descriptor) : holder.GetPropertyEntry(target, descriptor));
                                            foreach (string variable in variables.Where(v => expressions.Any(e => ((string)e["value"]!).IndexOf("<" + v + ">", StringComparison.Ordinal) >= 0)))
                                            { foreach (JToken actual in Actual(target, id.AsInt, index)) { bindings.Add(new JObject { ["variable"] = variable, ["target"] = Add(target), ["propertyId"] = id.AsInt, ["index"] = index, ["language"] = actual["language"]!.DeepClone() }); } }
                                        }
                                    });
                                }
                            }
                            finally { foreach (AnyPropertyId id in ids) { id.Dispose(); } }
                        });
                    }
                    foreach (string variable in variables.Where(v => !bindings.Any(b => (string?)b["variable"] == v)))
                    { Gap("placeholderBindings", "/placeholders", "Unresolved variable binding: " + variable); }
                });
            }
        }
        finally
        {
            foreach (UniversalPropertyList list in lists.Values)
            { try { list.Dispose(); } catch (Exception) { result.Coverage["properties"] = false; result.Gaps.Add(new { pointer = "/objects", message = "A native property list could not be released." }); } }
        }
        Check();
        if (((JObject)EplanMcp.Bridge.Core.Protocol.BridgeJson.FromObject(result)).DescendantsAndSelf().OfType<JArray>().Sum(a => a.Count) > 10000)
        { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Module evidence exceeds 10,000 details; select a smaller scope."); }
        result.Complete = result.Coverage.Values.All(v => v) && result.Gaps.Count == 0;
        return result;
    }
}
