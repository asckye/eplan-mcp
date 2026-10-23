using System.Collections.Immutable;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Projects Bridge results into stable facts. Details are a closed whitelist, never an opaque payload.</summary>
internal static class PlanStepSemantics
{
    public static PlanStep Build(PlanBaseline baseline, PlanArgumentStep input, PlanImpact impact, WritePlan plan)
    {
        string[] keys = input.Op switch
        {
            "device.update" => ["functions", "pageCount", "pages"],
            "macro.insert" => ["macro", "deviceCount", "devices", "objectCount", "pageSizeKnown"],
            "page.delete" => ["pages", "orphaned", "orphanedCount"],
            "page.renumber" => ["numbering", "start", "step", "keepText", "subpages"],
            "device.renumber" => ["devices", "deviceCount", "pageCount", "onlyUnnumbered", "scheme", "start", "step"],
            _ => [],
        };
        ImmutableDictionary<string, string> details = (plan.Details ?? []).Where(p => keys.Contains(p.Key, StringComparer.Ordinal))
            .ToImmutableDictionary(StringComparer.Ordinal);
        var expected = new List<JsonElement>();
        JsonElement a = input.Args;
        SnapshotObject[] objects = [.. baseline.Snapshot.Pages.SelectMany(p => p.Objects)];
        object Target(SnapshotObject obj) => PlanScopeRules.Locator(baseline.Snapshot.Project.Path, obj);
        void Add(string tag, object target, object? value, string[] properties, SnapshotObject? obj = null) => expected.Add(JsonSerializer.SerializeToElement(new
        {
            tag,
            target,
            value,
            coveredProperties = properties,
            source = new { step = input.Index, operation = input.Op + ".plan", evidence = obj is not null ? "baseline.standard" : tag == "orphaned" ? OperationNames.PlanFunctions : "args", locator = obj is null ? null : Target(obj), hash = obj?.Hash },
        }, PlanJson.Options));
        SnapshotObject Page(string name) => objects.FirstOrDefault(o => o.Kind == "page" && o.Name == name)
            ?? objects.Single(o => o.Kind == "page" && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
        string language = a.TryGetProperty("language", out JsonElement lang) ? lang.GetString()! : "";
        string description = "values/11011/" + language;
        switch (input.Op)
        {
            case "page.create":
                foreach (JsonElement page in a.GetProperty("pages").EnumerateArray())
                {
                    Add("exists", new { kind = "page", name = page.GetProperty("name").GetString() }, new
                    { pageType = page.GetProperty("page_type").GetString(), language, description = Tools.ToolArguments.Optional(page.GetProperty("description").GetString()) },
                        ["exists", "pageType", description]);
                }
                break;
            case "page.update":
                foreach (JsonElement page in a.GetProperty("pages").EnumerateArray())
                {
                    SnapshotObject obj = Page(page.GetProperty("name").GetString()!);
                    Add("value", Target(obj), new { language, description = Tools.ToolArguments.Optional(page.GetProperty("description").GetString()) }, [description], obj);
                }
                break;
            case "device.update":
                foreach (SnapshotObject obj in PlanScopeRules.Select(baseline.Snapshot, impact.ModifyScope))
                {
                    Add("value", Target(obj), new { name = plan.Changes.Single().NewValue }, ["name"], obj);
                }

                break;
            case "page.delete":
                string[] deleted = [.. plan.Changes.Select(c => c.Name)];
                foreach (SnapshotObject obj in objects.Where(o => deleted.Contains(o.Kind == "page" ? o.Name : o.Page, StringComparer.Ordinal)))
                {
                    Add("deleted", Target(obj), false, ["exists"], obj);
                }

                foreach (PlanOrphan orphan in impact.Orphaned)
                {
                    Add("orphaned", new { kind = "device", name = orphan.Name }, new { hasMain = false, remaining = orphan.Remaining }, ["main.exists", "functions.exists"]);
                }

                break;
            case "page.renumber":
            case "device.renumber":
                Add("numbering", impact.ModifyScope, new { args = a, exactNames = false, mayRemainUnnumbered = input.Op == "device.renumber" },
                    [input.Op == "page.renumber" ? "page.name" : "function.name"]);
                break;
            case "macro.insert":
                // Existing plan details describe a bounded sample, not all inserted functions or graphical objects.
                Add("exists", new { kind = "macro", page = plan.Changes.Single().Name }, new { args = a, details }, ["page.exists"]);
                break;
        }
        string[] gaps = input.Op == "macro.insert" ? ["macro.insertion", "macro.functions", "macro.graphics", "macro.placeholders", "macro.fileHash"]
            : input.Op is "page.renumber" or "device.renumber" ? ["numbering.exactNames"] : [];
        return new(input.Index, input.Op, a.Clone(), PlanStepCatalog.Tier(input.Op), impact.ReadScope, impact.ModifyScope, impact.Preconditions,
            [.. expected], [.. plan.Changes.Select(c => new PlanChange(c.Action, c.Kind, c.Name, c.Property, c.OldValue, c.NewValue))], input.DependsOn, null, plan.Changes.Count > 0)
        {
            Details = details,
            Coverage = JsonSerializer.SerializeToElement(new
            {
                baselinePages = impact.ReadScope.Pages,
                pages = impact.ReadScope.Pages.Concat(impact.ModifyScope.Pages).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                profile = baseline.Snapshot.Profile,
                properties = expected.SelectMany(e => e.GetProperty("coveredProperties").EnumerateArray().Select(p => p.GetString()!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                unknown = gaps.Concat(["graphics", "texts", "placeholders", "connections.withoutPage"]),
                skippedConnections = baseline.Snapshot.SkippedConnections,
            }, PlanJson.Options),
        };
    }
}
