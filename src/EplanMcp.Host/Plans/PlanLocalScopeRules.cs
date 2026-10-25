using System.Collections.Immutable;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>LP-3 scope semantics, kept separate from the version 1 executable compiler until LP-9.</summary>
internal static class PlanLocalScopeRules
{
    public static PlanLocalImpact Resolve(PlanProject project, PlanImpactRequest request, IEnumerable<string> projectPages,
        IEnumerable<PlanFunction>? closure = null)
    {
        PlanFunction[] functions = [.. closure ?? []];
        PlanScope targets = request.Targets;
        PlanScope read;
        PlanScope modify;
        switch (request.Op)
        {
            case "page.create":
                Require(targets.Pages.Length is >= 1 and <= 50, "/args/pages");
                read = PlanScopeRules.Pages([]);
                modify = PlanScopeRules.Pages(targets.Pages);
                break;
            case "device.update":
                Require(functions.Length <= 200, "/modifyScope/locators");
                if (functions.Length == 0 || !functions.Any(f => f.IsMain)
                    || functions.Any(f => f.Kind != "function" || f.Type <= 0 || f.Oid <= 0 || f.Name != request.DeviceName)
                    || functions.Select(f => (f.Type, f.Oid)).Distinct().Count() != functions.Length)
                {
                    throw Missing("plan.functions:device");
                }
                read = PlanScopeRules.Pages(functions.Select(f => f.Page).OfType<string>().Where(p => p.Length > 0));
                modify = new([], [], [.. functions.OrderBy(f => f.Type).ThenBy(f => f.Oid).Select(f =>
                    new PlanLocator(PlanSnapshotHashes.ProjectKey(project.Path), f.Kind, f.Type, f.Oid, f.Name, f.Page, null, null))]);
                break;
            case "macro.insert":
                Require(targets.Pages.Length == 1, "/args/page");
                read = PlanScopeRules.Pages(targets.Pages);
                modify = read;
                break;
            case "page.renumber":
            case "device.renumber":
                Require(targets.Pages.Length is >= 1 and <= 50, "/args/pages");
                read = PlanScopeRules.Pages(projectPages);
                RenumberLimit(read.Pages.Length, 0);
                modify = read;
                break;
            default:
                throw new ArgumentException("This local reader supports the three Write steps and renumber steps.", nameof(request));
        }
        Require(read.Pages.Length <= 50, "/readScope/pages");
        return new(request, read, modify, [.. functions]);
    }

    public static PlanImpact Build(PlanBaseline baseline, PlanLocalImpact impact, WritePlan plan, PlanMacroInspectData? macro = null)
    {
        PlanSnapshot snapshot = baseline.Snapshot;
        if (plan.Project != snapshot.Project.Name || !Path.IsPathFullyQualified(plan.ProjectPath)
            || PlanSnapshotHashes.ProjectKey(plan.ProjectPath) != snapshot.Project.Path)
        {
            throw Stale("The planning project changed; compile again.");
        }
        PlanScope read = impact.ReadScope;
        SnapshotObject[] objects = Validate(baseline, impact);
        IEnumerable<PlanOccupiedName> absent = [];
        if (impact.Request.Op == "page.create")
        {
            absent = impact.ModifyScope.Pages.Select(p => new PlanOccupiedName("page", p));
        }
        else if (impact.Request.Op == "device.update")
        {
            PlannedChange change = plan.Changes.Single();
            if (change.Name != impact.Request.DeviceName || string.IsNullOrEmpty(change.NewValue))
            {
                throw Stale("Use the resolved full names from device.update.plan and compile again.");
            }
            absent = [new("function", change.NewValue)];
        }
        else if (impact.Request.Op == "macro.insert")
        {
            if (objects.Length != 1 || objects[0].Kind != "page" || objects[0].PageType != "Circuit")
            {
                throw Missing("macro:empty_circuit_page");
            }
            absent = Candidates(read.Pages.Single(), macro).Select(n => new PlanOccupiedName("function", n));
        }
        ImmutableArray<PlanPrecondition> conditions = PlanPreconditions.Build(baseline, read, absent,
            detail: impact.ModifyScope);
        PlanPageHash[] pageHashes = [.. snapshot.Pages.Where(p => read.Pages.Contains(p.Name, StringComparer.Ordinal))
            .Select(p => new PlanPageHash(PlanScopeRules.Locator(snapshot.Project.Path, p.Objects.Single(o => o.Kind == "page")), p.Hash))];
        return new(read, impact.ModifyScope, [.. conditions.Concat(pageHashes).Distinct()
            .Concat(macro is null ? [] : [new PlanResourceHash(macro.Sha256)])], baseline.NamesHash, []);
    }

    internal static SnapshotObject[] Validate(PlanBaseline baseline, PlanLocalImpact impact)
    {
        SnapshotObject[] objects = [.. PlanScopeRules.Select(baseline.Snapshot, impact.ReadScope)];
        if (impact.Request.Op is "page.renumber" or "device.renumber")
        {
            RenumberLimit(impact.ReadScope.Pages.Length, objects.Length);
        }
        Require(objects.Length <= 10_000, "/readScope/objects");
        if (!PlanScopeRules.Missing(baseline, impact.ReadScope).IsEmpty)
        {
            throw Stale("An impact page is missing; compile again.");
        }
        if (impact.Request.Op == "device.update")
        {
            if (impact.Functions.Any(f => f.Page is null or ""))
            {
                throw Missing("object.resolve:functions_without_page");
            }
            var expected = impact.Functions.Select(f => (f.Kind, f.Type, f.Oid, f.Name, Main: (bool?)f.IsMain, f.Page)).ToHashSet();
            var actual = objects.Where(o => o.Kind == "function" && o.Name == impact.Request.DeviceName)
                .Select(o => (o.Kind, o.Type, o.Oid, Name: o.Name!, o.Main, o.Page)).ToHashSet();
            if (!expected.SetEquals(actual))
            {
                throw Stale("The complete same-name function closure changed; compile again.");
            }
        }
        return objects;
    }

    public static ImmutableArray<string> Candidates(string page, PlanMacroInspectData? macro)
    {
        if (macro is null || macro.Sha256.Length != 64 || macro.Sha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || macro.ObjectCount < macro.Functions.Count || macro.Functions.Count > 10_000
            || macro.Functions.Any(f => f.Category.StartsWith("PLCTerminal", StringComparison.Ordinal)
                || f.Kind is not ("function" or "terminal" or "plc" or "cable")
                || f.Category.Length == 0 || f.StructureSegments.Any(s => s.Length == 0 || s[0] is not ('=' or '+'))
                || f.StructureSegments.GroupBy(s => s[0]).Any(g => g.Count() > 1)
                || string.Concat(f.StructureSegments) != f.Name.Split('-')[0]))
        {
            throw Missing("macro:finite_candidate_names");
        }
        ImmutableArray<string> names = [.. macro.Functions.Where(f => f.Name.Length > 0)
            .Select(f => PlanNamePattern.Name(page, f)).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        if (names.Length > 200)
        {
            throw Missing("macro:candidate_names_limit");
        }
        return names;
    }

    internal static void RenumberLimit(int pages, int objects)
    {
        if (pages > 50 || objects > 10_000)
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.CapabilityMissing,
                Message = "Whole-project renumber coverage exceeds plan limits. Use eplan_page_renumber or eplan_device_renumber with their approval and backup checks.",
                Metadata = new()
                {
                    ["missing"] = "plan.renumber:whole_project_guard",
                    ["pages"] = pages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["objects"] = pages > 50 && objects == 0 ? "not_read" : objects.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["limits"] = "50 pages / 10000 objects",
                },
            });
        }
    }

    private static void Require(bool valid, string pointer)
    {
        if (!valid)
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.InvalidArgument,
                Message = "Narrow the step to the plan scope limits.",
                Metadata = new()
                {
                    ["pointer"] = pointer,
                },
            });
        }
    }

    internal static BridgeCallException Missing(string capability) => new(new BridgeError
    {
        Reason = ErrorReason.CapabilityMissing,
        Message = "Complete local plan evidence is unavailable. Use a compatible Bridge and a supported scope.",
        Metadata = new()
        {
            ["missing"] = capability,
        },
    });

    internal static BridgeCallException Stale(string message) => new(new BridgeError
    {
        Reason = ErrorReason.PlanStale,
        Message = message,
    });
}

/// <summary>Transient discovery only; persisted evidence remains the existing PlanBaseline and six precondition tags.</summary>
internal sealed record PlanLocalImpact(PlanImpactRequest Request, PlanScope ReadScope, PlanScope ModifyScope, ImmutableArray<PlanFunction> Functions);
