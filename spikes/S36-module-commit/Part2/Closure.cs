using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S36.Part2;

internal sealed partial class Probe
{
    private static JObject Languages(MultiLangString text)
    {
        var list = new LanguageList();
        int count = text.GetLanguageList(ref list);
        var result = new JObject();
        foreach (ISOCode.Language language in Enumerable.Range(0, count).Select(list.get_Language).OrderBy(l => (int)l))
        {
            using var code = new ISOCode(language);
            result[code.GetString()] = text.GetString(language); // GetString, not fallback display text.
        }
        return result;
    }
    private JObject Actual(StorableObject obj, int id, int index)
    {
        var row = new ReadRecord();
        row.Field("propertyId", () => id);
        row.Field("index", () => index);
        row.Get<object?>("value", () =>
        {
            if (id == 0 && obj is Text freeText)
            {
                using MultiLangString? content = freeText.Contents;
                row.Data["languages"] = content is null ? null : Languages(content);
                row.Data["available"] = content is not null;
                return null;
            }
            using PropertyValue? value = Property(obj, id, index);
            row.Data["available"] = value is not null;
            if (value is null) { return null; }
            row.Data["empty"] = value.IsEmpty;
            if (value.IsEmpty) { return null; }
            row.Data["raw"] = value.ToString();
            using PropertyDefinition definition = value.Definition;
            row.Data["type"] = definition.Type.ToString();
            if (id == 20093) { row.Data["boolean"] = value.ToBool(); }
            if (definition.Type.ToString() == "MultilangString")
            {
                using MultiLangString? text = value.ToMultiLangString();
                row.Data["languages"] = text is null ? null : Languages(text);
            }
            return null;
        });
        return row.Data;
    }
    private JObject Locator(StorableObject obj, Page inserted)
    {
        var row = new ReadRecord();
        row.Field("type", () => obj.GetType().FullName);
        row.Field("oid", () => obj.ObjectIdentifier);
        Page? page = row.Get<Page?>("page", () => obj is Page p ? p : (obj as Placement)?.Page);
        row.Field("page", () => page?.Name);
        row.Field("pageOid", () => page?.ObjectIdentifier);
        row.Field("outsideInsertedPage", () => page is null || page.ObjectIdentifier != inserted.ObjectIdentifier);
        row.Field("scope", () => obj is Project ? "project" : obj is Page ? "page" : page is null ? "unplaced" : "placement");
        if (obj is Function f)
        {
            row.Field("name", () => f.Name);
            row.Field("main", () => f.IsMainFunction);
            row.Field("representation20121", () => Actual(f, 20121, 0)); // FUNC_TYPE: 1 multiline, 3 overview, -2 pair cross-reference.
            row.Array("articleReferences", () => f.ArticleReferences, r => new JObject { ["part"] = r.PartNr, ["variant"] = r.VariantNr, ["count"] = r.Count });
        }
        return row.Data;
    }
    private IEnumerable<(int Id, int Index)> Descriptors(StorableObject obj, ReadRecord evidence)
    {
        var result = new HashSet<(int Id, int Index)>(_descriptors);
        AnyPropertyId[]? ids = evidence.Get<AnyPropertyId[]?>("existingIds", () => Properties(obj).ExistingIds);
        if (ids is null) { evidence.Data["missingPropertyDirectory"] = true; return result; }
        // Dispose the entire returned array even when one descriptor cannot be decoded.
        try
        {
            foreach (AnyPropertyId? id in ids)
            {
                if (id is null) { evidence.Data["nullPropertyDescriptor"] = true; continue; }
                evidence.Get<object?>("descriptor", () =>
                {
                    int number = id.AsInt;
                    using PropertyDefinition definition = id.Definition;
                    if (!definition.IsIndexed) { result.Add((number, 0)); }
                    else if (id.Index > 0) { result.Add((number, id.Index)); }
                    else
                    {
                        using AnyPropertyId unindexed = number;
                        using PropertyValue? indexed = Properties(obj)[unindexed];
                        foreach (int index in indexed?.Indexes ?? []) { result.Add((number, index)); }
                    }
                    return null;
                });
            }
        }
        finally { foreach (AnyPropertyId? id in ids) { id?.Dispose(); } }
        return result.OrderBy(d => d.Id).ThenBy(d => d.Index);
    }
    private JObject ValueTable(PlaceHolder holder)
    {
        var row = new ReadRecord();
        row.Field("name", () => holder.Name);
        row.Array("variables", () => holder.VariableNames?.Cast<string>().OrderBy(v => v, StringComparer.Ordinal), v => new JValue(v));
        row.Array("records", () => Enumerable.Range(0, holder.NumberOfRecords), i =>
        {
            var record = new ReadRecord();
            string? name = record.Get<string?>("name", () => holder.get_NameOfRecord(i));
            record.Data["name"] = name;
            record.Array("values", () => holder.VariableNames?.Cast<string>().OrderBy(v => v, StringComparer.Ordinal), variable =>
            {
                using MultiLangString? value = holder.GetValue(name!, variable);
                return new JObject { ["variable"] = variable, ["languages"] = value is null ? null : Languages(value) };
            });
            return record.Data;
        });
        return row.Data;
    }
    private Function[] FunctionInventory(ReadRecord evidence)
    {
        using var finder = new DMObjectsFinder(project);
        Function[]? found = evidence.Get<Function[]?>("finderFunctions", () => finder.GetFunctions(null));
        Page[]? pages = evidence.Get<Page[]?>("inventoryPages", () => project.Pages);
        if (found is null || pages is null) { evidence.Data["missingFunctionInventory"] = true; }
        var result = new Dictionary<string, Function>(StringComparer.Ordinal);
        void Add(IEnumerable<Function?> candidates)
        {
            foreach (Function? function in candidates)
            {
                if (function is null) { evidence.Data["missingFunctionInventory"] = true; continue; }
                string? key = evidence.Get<string?>("functionIdentifier", function.ToStringIdentifier);
                if (key is null) { evidence.Data["missingFunctionInventory"] = true; }
                else { result[key] = function; }
            }
        }
        Add(found ?? []);
        foreach (Page? page in pages ?? [])
        {
            if (page is null) { evidence.Data["missingFunctionInventory"] = true; continue; }
            Function[]? functions = evidence.Get<Function[]?>("pageFunctions", () => page.Functions);
            Placement[]? placements = evidence.Get<Placement[]?>("pageAllPlacements", () => page.AllPlacements);
            if (functions is null || placements is null) { evidence.Data["missingFunctionInventory"] = true; }
            Add(functions ?? []);
            Add((placements ?? []).OfType<Function>()); // A12: finder/Page.Functions alone omit extra representations.
        }
        return result.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToArray();
    }
    internal JObject Closure()
    {
        Page inserted = Require(SourceCounter);
        var evidence = new ReadRecord();
        Function[] functions = FunctionInventory(evidence);
        var holders = new JArray();
        Placement[]? placements = evidence.Get<Placement[]?>("sourcePlacements", () => inserted.AllPlacements);
        if (placements is null) { evidence.Data["missingAssignedObjects"] = true; }
        foreach (PlaceHolder holder in (placements ?? []).OfType<PlaceHolder>().OrderBy(h => h.ObjectIdentifier))
        {
            var row = new ReadRecord();
            row.Data["holder"] = Locator(holder, inserted);
            row.Data["table"] = ValueTable(holder);
            row.Field("referenceCount", () => holder.NumberOfReferences);
            StorableObject[]? assigned = row.Get<StorableObject[]?>("assignedObjects", () => holder.AssignedObjects);
            if (assigned is null) { row.Data["missingAssignedObjects"] = true; }
            row.Array("assigned", () => assigned?.OrderBy(o => o?.ObjectIdentifier), o => Locator(o, inserted));
            var bindings = new JArray();
            // Project entries use their separate accessor even when project is absent from AssignedObjects.
            StorableObject[] objects = (assigned ?? []).Where(o => o is not null).Concat(new StorableObject[] { inserted, project })
                .GroupBy(o => o.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()).ToArray();
            foreach (StorableObject obj in objects)
            {
                foreach ((int id, int index) in Descriptors(obj, row))
                {
                    row.Get<object?>("entry:" + Evidence.Number(id) + ":" + Evidence.Number(index), () =>
                    {
                        using AnyPropertyId property = Id(id, index);
                        using MultiLangString? entry = obj is Project ? holder.GetProjectPropertyEntry(property) : holder.GetPropertyEntry(obj, property);
                        if (entry is null) { return null; }
                        JObject entries = Languages(entry);
                        if (!entries.HasValues) { return null; }
                        string[] variables = holder.VariableNames.Cast<string>().Where(v => entries.Properties().Any(p =>
                            ((string?)p.Value)?.IndexOf("<" + v + ">", StringComparison.Ordinal) >= 0)).OrderBy(v => v, StringComparer.Ordinal).ToArray();
                        if (variables.Length == 0) { return null; }
                        bindings.Add(new JObject { ["variables"] = new JArray(variables), ["entry"] = entries, ["object"] = Locator(obj, inserted),
                            ["propertyId"] = id, ["index"] = index, ["actual"] = Actual(obj, id, index), ["related"] = Related(obj, id, index, inserted, functions) });
                        return null;
                    });
                }
            }
            row.Data["bindings"] = bindings;
            row.Array("unmappedVariables", () => holder.VariableNames.Cast<string>().Where(v => !bindings.Any(b => ((JArray)b["variables"]!).Values<string>().Contains(v, StringComparer.Ordinal))), v => new JValue(v));
            holders.Add(row.Data);
        }
        evidence.Data["holders"] = holders;
        evidence.Data["candidateBasis"] = "ExistingIds + used indexes + all shipped XML binding descriptors + explicit probe bindings";
        evidence.Data["complete"] = holders.Count > 0 && !HasReadGap(evidence.Data) && holders.All(h => !((JArray)h["unmappedVariables"]!).Any());
        return evidence.Data;
    }
    private JArray Related(StorableObject root, int id, int index, Page inserted, Function[] functions)
    {
        var result = new JArray();
        var queue = new Queue<StorableObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { root.ToStringIdentifier() };
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            StorableObject obj = queue.Dequeue();
            var row = new ReadRecord();
            StorableObject[]? cross = row.Get<StorableObject[]?>("crossReferencedObjectsAll", () => obj.CrossReferencedObjectsAll);
            if (cross is null) { row.Data["missingCrossReferences"] = true; }
            IEnumerable<StorableObject> peers = cross ?? [];
            if (obj is Function f)
            {
                Function[]? sub = row.Get<Function[]?>("allSubFunctions", () => f.AllSubFunctions);
                Function? parent = row.Get<Function?>("parentFunction", () => f.ParentFunction);
                peers = peers.Concat(sub ?? []).Concat(parent is null ? [] : new[] { parent })
                    .Concat(functions.Where(p => p.Name == f.Name));
            }
            row.Data["source"] = Locator(obj, inserted);
            row.Array("peers", () => peers.Where(p => p is not null).GroupBy(p => p.ToStringIdentifier(), StringComparer.Ordinal)
                .Select(g => g.First()).OrderBy(p => p.ObjectIdentifier), peer =>
            {
                if (seen.Add(peer.ToStringIdentifier())) { queue.Enqueue(peer); }
                return new JObject { ["object"] = Locator(peer, inserted), ["actual"] = Actual(peer, id, index), ["directBinding"] = false };
            });
            result.Add(row.Data);
        }
        return result;
    }
    private static bool HasReadGap(JToken token) => token is JContainer container && container.DescendantsAndSelf().OfType<JProperty>().Any(p =>
        p.Name is "readErrors" or "missingPropertyDirectory" or "nullPropertyDescriptor" or "missingFunctionInventory" or "missingAssignedObjects" or "missingCrossReferences" ||
        (p.Name == "available" && (bool?)p.Value == false) ||
        (p.Name is "variables" or "languages" or "articleReferences" && p.Value.Type == JTokenType.Null));
    internal JObject Read()
    {
        var row = new ReadRecord();
        row.Field("pageCount", () => project.Pages?.Length);
        row.Array("reservedPages", () => project.Pages?.Where(p => p is not null && Counter(p) is >= SentinelCounter and <= TargetCounter).OrderBy(p => Counter(p)), page =>
        {
            var state = new ReadRecord();
            state.Data["page"] = Locator(page, page);
            state.Data["description"] = Actual(page, 11011, 0);
            state.Array("placements", () => page.AllPlacements?.OrderBy(p => p.ObjectIdentifier), p => Locator(p, page));
            state.Array("functions", () => (page.Functions ?? []).Concat((page.AllPlacements ?? []).OfType<Function>()).Where(f => f is not null)
                .GroupBy(f => f.ToStringIdentifier(), StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.ObjectIdentifier), f => new JObject { ["object"] = Locator(f, page),
                ["properties"] = new JArray(_bindings.Select(b => Actual(f, b.Id, b.Index))) });
            state.Array("holders", () => page.AllPlacements?.OfType<PlaceHolder>().OrderBy(h => h.ObjectIdentifier), ValueTable);
            return state.Data;
        });
        row.Data["projectField"] = Actual(project, 10901, 1000);
        if (Find(SourceCounter) is not null) { row.Data["closure"] = Closure(); }
        return row.Data;
    }
}
