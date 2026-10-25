using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

internal static class PlanVerificationFacts
{
    internal sealed record Fact(JsonElement Locator, string Property, string? Expected, string? Actual, string Status, string Reason);

    internal static Dictionary<string, SnapshotObject> Objects(PlanEvidence? evidence) => evidence?.Snapshot.Pages.SelectMany(p => p.Objects)
        .ToDictionary(o => PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), StringComparer.Ordinal) ?? new(StringComparer.Ordinal);

    internal static Dictionary<string, string?> Properties(SnapshotObject obj)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (JsonProperty p in JsonSerializer.SerializeToElement(obj, PlanJson.Options).EnumerateObject())
        {
            if (p.Name is not ("hash" or "kind" or "type" or "oid" or "values"))
            {
                result[p.Name] = p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.GetRawText();
            }
        }
        foreach (SnapshotValue value in obj.Values)
        {
            string key = "values/" + value.Id.ToString(CultureInfo.InvariantCulture)
                + (value.Index is { } index ? "/" + index.ToString(CultureInfo.InvariantCulture) : "");
            if (value.Texts is { } texts)
            {
                foreach (SnapshotText text in texts)
                {
                    result[key + "/" + text.Language] = JsonSerializer.Serialize(text.Text);
                }
            }
            else
            {
                result[key] = value.Value is null ? null : JsonSerializer.Serialize(value.Value);
            }
        }
        return result;
    }

    internal static bool Targets(JsonElement target, SnapshotObject obj) =>
        target.TryGetProperty("oid", out JsonElement oid)
            ? oid.GetInt64() == obj.Oid && target.GetProperty("type").GetInt32() == obj.Type && target.GetProperty("kind").GetString() == obj.Kind
            : target.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == obj.Kind
                && target.TryGetProperty("name", out JsonElement name) && name.GetString() == obj.Name;

    internal static List<Fact> Check(PlanStep step, PlanEvidence baseline, PlanEvidence? after)
    {
        var facts = new List<Fact>();
        SnapshotObject[] objects = [.. Objects(after).Values];
        foreach (JsonElement expected in step.Expected)
        {
            JsonElement target = expected.GetProperty("target"), value = expected.GetProperty("value");
            string tag = expected.GetProperty("tag").GetString()!;
            SnapshotObject? obj = objects.SingleOrDefault(o => Targets(target, o));
            string? page = target.TryGetProperty("page", out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString()
                : target.TryGetProperty("name", out JsonElement n) && target.TryGetProperty("kind", out JsonElement k) && k.GetString() == "page" ? n.GetString() : null;
            bool observed = after is not null && after.Covers(page);
            void Add(string property, string? wanted, string? actual, bool known, bool absent = false)
            {
                string status = "unknown";
                string reason = "unobserved";
                if (known)
                {
                    status = !absent && wanted == actual ? "passed" : "failed";
                    reason = absent ? "object_absent" : wanted == actual ? "proven" : "mismatch";
                }
                facts.Add(new(target, property, wanted, known ? actual : null, status, reason));
            }
            if (tag == "numbering")
            {
                SnapshotObject[] numbered = [.. PlanScopeRules.Select(baseline.Snapshot, step.ModifyScope)
                    .Where(o => step.Op == "page.renumber" ? o.Kind == "page" : o.Kind != "page" && o.Kind != "connection")];
                foreach (SnapshotObject old in numbered)
                {
                    SnapshotObject? current = objects.SingleOrDefault(o => o.Kind == old.Kind && o.Type == old.Type && o.Oid == old.Oid);
                    facts.Add(new(JsonSerializer.SerializeToElement(PlanScopeRules.Locator(baseline.Snapshot.Project.Path, old), PlanJson.Options),
                        "name", "nonempty", current?.Name, current is null && after?.Covers(old.Kind == "page" ? old.Name : old.Page) != true ? "unknown"
                            : string.IsNullOrEmpty(current?.Name) ? "failed" : "passed", "server_numbering"));
                }
                if (numbered.Length == 0)
                {
                    Add("numbering", "observable_objects", null, false);
                }
                continue;
            }
            if (tag == "orphaned")
            {
                SnapshotObject[] functions = [.. objects.Where(o => o.Kind != "page" && o.Name == target.GetProperty("name").GetString())];
                Add("main.exists", "false", functions.Any(o => o.Main == true) ? "true" : "false", after is not null && after.Covers(null));
                foreach (JsonElement remaining in value.GetProperty("remaining").EnumerateArray())
                {
                    Add("functions.exists", "true", functions.Any(o => Targets(remaining, o)) ? "true" : "false", after is not null && after.Covers(null));
                }
                continue;
            }
            if (tag == "macro" || step.Op == "macro.insert")
            {
                string macroPage = step.Args.GetProperty("page").GetString()!;
                Add("page.exists", "true", objects.Any(o => o.Kind == "page" && o.Name == macroPage) ? "true" : "false", after?.Covers(macroPage) == true);
                if (tag == "macro")
                {
                    Add("pageType", value.GetProperty("pageType").GetString(), objects.SingleOrDefault(o => o.Kind == "page" && o.Name == macroPage)?.PageType, after?.Covers(macroPage) == true);
                    JsonElement[] slots = [.. value.GetProperty("functions").EnumerateArray()];
                    SnapshotObject[] actual = [.. objects.Where(o => o.Page == macroPage && o.Kind != "connection")];
                    int[] owners = [.. Enumerable.Repeat(-1, actual.Length)];
                    bool Assign(int slot, HashSet<int> seen)
                    {
                        for (int i = 0; i < actual.Length; i++)
                        {
                            JsonElement constraint = slots[slot];
                            SnapshotObject f = actual[i];
                            if (!seen.Contains(i) && f.Kind == constraint.GetProperty("kind").GetString() && f.Category == constraint.GetProperty("category").GetString()
                                && f.Main == constraint.GetProperty("main").GetBoolean() && PlanNamePattern.Matches(constraint.GetProperty("namePattern").GetString()!, f.Name ?? "")
                                && PlanNamePattern.Matches(constraint.GetProperty("visibleNamePattern").GetString()!, f.Shown ?? ""))
                            {
                                seen.Add(i);
                                if (owners[i] < 0 || Assign(owners[i], seen))
                                {
                                    owners[i] = slot;
                                    return true;
                                }
                            }
                        }
                        return false;
                    }
                    Add("macro.functions", "true", actual.Length == slots.Length && Enumerable.Range(0, slots.Length).All(i => Assign(i, [])) ? "true" : "false", after?.Covers(macroPage) == true);
                }
                else
                {
                    Add("macro.functions", "complete_inventory", null, false);
                }
                Add("macro.graphics", "verified", null, false);
                Add("macro.placeholders", "verified", null, false);
                Add("macro.fileHash", "verified", null, false);
                Add("macro.position", "verified", null, false);
                continue;
            }
            Add("exists", tag == "deleted" ? "false" : "true", obj is null ? "false" : "true", obj is not null || observed);
            if (tag == "deleted")
            {
                continue;
            }
            Dictionary<string, string?> properties = obj is null ? new(StringComparer.Ordinal) : Properties(obj);
            foreach (string property in expected.GetProperty("coveredProperties").EnumerateArray().Select(e => e.GetString()!).Where(s => s != "exists"))
            {
                string field = property.StartsWith("values/11011/", StringComparison.Ordinal) ? "description" : property;
                bool supported = value.TryGetProperty(field, out JsonElement desired);
                string? wanted = supported ? desired.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => JsonSerializer.Serialize(desired.GetString()),
                    _ => desired.GetRawText(),
                } : null;
                Add(property, wanted, properties.GetValueOrDefault(property), supported && (obj is not null || observed), obj is null);
            }
        }
        return facts;
    }
}
