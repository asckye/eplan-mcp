using System.Text.Json;

namespace EplanMcp.Catalog;

/// <summary>summary.json: counts per layer, object type, namespace and kind (design section 3).</summary>
internal static class Summary
{
    public static void Write(Utf8JsonWriter w, CatalogData catalog)
    {
        List<CatalogEntry> entries = catalog.Entries;
        var typeKinds = entries.Where(e => e.Id.StartsWith("T:", StringComparison.Ordinal)).ToDictionary(e => e.Id, e => e.Kind);
        w.WriteStartObject();
        w.WriteNumber("schemaVersion", 1);
        w.WriteString("namespacePrefix", catalog.NamespacePrefix);
        w.WriteStartObject("intellisense");
        w.WriteNumber("members", catalog.Xml.Members);
        w.WriteNumber("public", catalog.Xml.Public);
        w.WriteNumber("nonPublic", catalog.Xml.NonPublic);
        w.WriteNumber("outsideNamespace", catalog.Xml.OutsideNamespace);
        w.WriteNumber("unmatched", catalog.Xml.Unmatched.Count);
        w.WriteEndObject();

        w.WriteStartObject("entries");
        w.WriteNumber("total", entries.Count);
        w.WriteNumber("types", typeKinds.Count);
        w.WriteNumber("undocumented", entries.Count(e => !e.Documented));
        w.WriteNumber("duplicateIds", catalog.Duplicates);
        w.WriteNumber("publicTypesOutsidePrefix", catalog.OutsideTypes);
        Counts(w, "byKind", entries.GroupBy(e => e.Kind));
        w.WriteEndObject();

        w.WriteStartObject("layers");
        foreach (string layer in new[] { Classifier.Operation, Classifier.DataDictionary, Classifier.TypeLayer })
        {
            List<CatalogEntry> members = [.. entries.Where(e => e.Layer == layer)];
            w.WriteStartObject(layer);
            w.WriteNumber("members", members.Count);
            if (layer == Classifier.Operation)
            {
                List<IGrouping<string, CatalogEntry>> groups = [.. members.GroupBy(e => e.Group!)];
                w.WriteNumber("groups", groups.Count);
                w.WriteNumber("excludedMembers", members.Count(e => e.Excluded is not null));
                w.WriteNumber("denominatorGroups", groups.Count(g => g.Any(e => e.Excluded is null)));
                Counts(w, "excludedMembersByRule", members.Where(e => e.Excluded is not null).GroupBy(e => e.Excluded!.Rule));
                Counts(w, "denominatorGroupsByKind", members.Where(e => e.Excluded is null).DistinctBy(e => e.Group).GroupBy(e => e.Kind));
                w.WriteNumber("undocumentedDenominatorGroups", groups.Count(g => g.Any(e => e.Excluded is null) && !g.Any(e => e.Documented)));
                w.WriteNumber("groupsOnInterfaces", members.Where(e => typeKinds.GetValueOrDefault(e.DeclaringType!) == "interface")
                    .Select(e => e.Group).Distinct().Count());
            }

            if (layer == Classifier.DataDictionary)
            {
                List<DictionaryProperty> properties = DataDictionary.Build(members);
                w.WriteNumber("properties", properties.Count);
                w.WriteNumber("obsoleteProperties", properties.Count(p => p.Obsolete));
                w.WriteNumber("undocumentedProperties", properties.Count(p => !p.Documented));
                w.WriteNumber("objects", properties.Select(p => p.Object).Distinct().Count());
                w.WriteStartObject("byObject");
                foreach (IGrouping<string, DictionaryProperty> o in properties.GroupBy(p => p.Object))
                {
                    // The member IDs of a property follow from these types: P:<propertyList>.<NAME>, its (System.Int32)
                    // overload, and F:<enum>.<NAME>.
                    w.WriteStartObject(o.Key);
                    WriteTypes(w, "propertyList", o.Select(p => p.ListType));
                    WriteTypes(w, "enum", o.Select(p => p.EnumType));
                    w.WriteNumber("properties", o.Count());
                    w.WriteNumber("inPropertyList", o.Count(p => p.ListType is not null));
                    w.WriteNumber("inEnum", o.Count(p => p.EnumType is not null));
                    w.WriteNumber("obsolete", o.Count(p => p.Obsolete));
                    w.WriteEndObject();
                }

                w.WriteEndObject();
            }

            Counts(w, "byKind", members.GroupBy(e => e.Kind));
            w.WriteEndObject();
        }

        w.WriteEndObject();
        w.WriteStartObject("byNamespace");
        foreach (IGrouping<string, CatalogEntry> ns in entries.GroupBy(e => e.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            w.WriteStartObject(ns.Key);
            w.WriteNumber("types", ns.Count(e => typeKinds.ContainsKey(e.Id)));
            w.WriteNumber("operationGroups", ns.Where(e => e.Layer == Classifier.Operation).Select(e => e.Group).Distinct().Count());
            w.WriteNumber("operationDenominatorGroups", ns.Where(e => e.Layer == Classifier.Operation && e.Excluded is null)
                .Select(e => e.Group).Distinct().Count());
            w.WriteNumber("dataDictionaryMembers", ns.Count(e => e.Layer == Classifier.DataDictionary));
            w.WriteNumber("typeLayerMembers", ns.Count(e => e.Layer == Classifier.TypeLayer));
            w.WriteEndObject();
        }

        w.WriteEndObject();
        w.WriteEndObject();
    }

    // One type is the rule; an array would show that an object name joins property IDs of different types.
    private static void WriteTypes(Utf8JsonWriter w, string name, IEnumerable<string?> types)
    {
        List<string> distinct = [.. types.OfType<string>().Distinct().Order(StringComparer.Ordinal)];
        if (distinct.Count == 1)
        {
            w.WriteString(name, distinct[0]);
        }
        else if (distinct.Count > 1)
        {
            w.WriteStartArray(name);
            distinct.ForEach(w.WriteStringValue);
            w.WriteEndArray();
        }
    }

    private static void Counts(Utf8JsonWriter w, string name, IEnumerable<IGrouping<string, CatalogEntry>> groups)
    {
        w.WriteStartObject(name);
        foreach (IGrouping<string, CatalogEntry> g in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            w.WriteNumber(g.Key, g.Count());
        }

        w.WriteEndObject();
    }
}
