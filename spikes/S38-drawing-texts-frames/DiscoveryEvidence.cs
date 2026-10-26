using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S38;

/// <summary>Detached discovery decisions; no native API access, so report regressions can be checked offline.</summary>
internal static class DiscoveryEvidence
{
    internal static void ResolveScope(JObject binding, bool context)
    {
        string? sourceScope = binding["source"]?.Value<string>("scope");
        string scope = sourceScope is "page" or "project" ? sourceScope : "unknown";
        bool inferred = false;
        string evidence = scope == "unknown" ? "No resolved target source or catalog scope." : "Valid source object matches the target.";
        if (scope == "unknown" && sourceScope != "otherProject" && (context || binding.Value<string>("sourceStatus") != "valid"))
        {
            if (binding.Value<string>("pageCatalogName")?.StartsWith("PAGE_", StringComparison.Ordinal) == true) { scope = "page"; }
            else if (binding.Value<string>("projectCatalogName")?.StartsWith("PROJ_", StringComparison.Ordinal) == true) { scope = "project"; }
            if (scope != "unknown")
            {
                inferred = true;
                evidence = "Inferred from the displayed property's catalog PAGE_/PROJ_ name; native source is unavailable or contextual.";
            }
        }
        binding["scope"] = scope;
        binding["inferred"] = inferred;
        binding["scopeEvidence"] = evidence;
        binding["name"] = binding[scope == "project" ? "projectCatalogName" : "pageCatalogName"]?.DeepClone();
        binding["descriptorComplete"] = binding["propertyId"]?.Type == JTokenType.Integer && binding["index"]?.Type == JTokenType.Integer;
    }

    internal static void CompleteInventory(JObject inventory)
    {
        JObject[] rows = ((JArray)inventory["objects"]!).OfType<JObject>().ToArray();
        var issues = new JArray();
        foreach (JObject row in rows)
        {
            foreach (JProperty error in row.Descendants().OfType<JProperty>().Where(p => p.Name == "error"))
            {
                bool binding = error.Ancestors().OfType<JProperty>().Any(p => p.Name == "binding");
                bool descriptor = binding && error.Ancestors().OfType<JProperty>().Any(p => p.Name == "descriptor");
                string category = binding ? descriptor ? "descriptor" : "bindingDiagnostic" : "text";
                issues.Add(new JObject { ["oid"] = row["oid"]?.DeepClone(), ["field"] = error.Path, ["category"] = category,
                    ["diagnostic"] = binding && !descriptor, ["error"] = error.Value.DeepClone() });
            }
        }
        JObject[] bindings = rows.Select(r => r["binding"]).OfType<JObject>().ToArray();
        bool traversal = ((JArray)inventory["traversalErrors"]!).Count == 0;
        bool texts = !issues.OfType<JObject>().Any(i => i.Value<string>("category") == "text");
        bool descriptors = bindings.All(b => b.Value<bool?>("descriptorComplete") == true);
        inventory["fieldErrors"] = issues;
        inventory["completeness"] = new JObject
        {
            ["traversal"] = traversal, ["textReads"] = texts, ["bindingDescriptors"] = descriptors,
            ["nativeSources"] = bindings.All(b => b.Value<string>("sourceStatus") == "valid"),
            ["inferredScopes"] = bindings.Count(b => b.Value<bool?>("inferred") == true),
            ["unresolvedDescriptors"] = bindings.Count(b => b.Value<bool?>("descriptorComplete") != true),
            ["diagnosticErrors"] = issues.Count(i => i.Value<bool>("diagnostic")),
            ["requiredErrors"] = issues.Count(i => !i.Value<bool>("diagnostic")),
        };
        inventory["countsByOrigin"] = new JObject(rows.GroupBy(r => r.Value<string>("origin")!, StringComparer.Ordinal)
            .Select(g => new JProperty(g.Key, new JObject(g.GroupBy(r => r.Value<string>("kind")!, StringComparer.Ordinal).Select(k => new JProperty(k.Key, k.Count()))))));
        // Source lookup is optional for contextual texts. An unreadable descriptor still makes discovery incomplete.
        inventory["complete"] = traversal && texts && descriptors;
    }

    internal static void ClassifyCandidate(JObject binding)
    {
        var reasons = new JArray();
        if (binding.Value<bool?>("descriptorComplete") != true) { reasons.Add("binding_descriptor_unavailable"); }
        if (binding.Value<string>("scope") is not ("page" or "project")) { reasons.Add("scope_unresolved"); }
        if (binding.Value<int?>("propertyId") == 11016) { reasons.Add("frame_switch_has_its_own_probe"); }
        if (binding["value"] is not JObject value || value["error"] is not null) { reasons.Add("property_value_unavailable"); }
        else
        {
            if (value.Value<bool?>("readOnly") != false) { reasons.Add("read_only"); }
            if (value.Value<bool?>("namePart") != false) { reasons.Add("name_part"); }
            if (value.Value<bool?>("internal") != false) { reasons.Add("internal"); }
            if (value.Value<string>("type") is not ("String" or "MultilangString")) { reasons.Add("unsupported_type"); }
            if (value.Value<bool?>("indexed") != false && binding.Value<int?>("index") is not > 0) { reasons.Add("index_unresolved"); }
        }
        binding["candidateRejectionReasons"] = reasons;
        binding["eligible"] = reasons.Count == 0;
    }
}
