using System.Collections.Immutable;
using System.Globalization;

namespace EplanMcp.Host.Plans;

internal sealed record PlanMacroTarget(string PageType, bool Empty, string Representation);

/// <summary>Checks resolved scopes after real planning; only the two closed prediction edges are admissible.</summary>
internal static class PlanDependencies
{
    public static ImmutableArray<PlanViolation> Check(ImmutableArray<PlanStep> steps, IReadOnlyDictionary<int, PlanMacroTarget> macroTargets)
    {
        var errors = new List<PlanViolation>();
        foreach (PlanStep step in steps.Where(s => s.Effective))
        {
            string at = "/steps/" + (step.Index - 1).ToString(CultureInfo.InvariantCulture);
            if (step.Op is "page.renumber" or "device.renumber" && steps.Any(s => s.Effective && s.Index > step.Index))
            { errors.Add(new(at + "/op", "Renumbering must be the last effective step.")); }
            if (step.ModifyScope.Pages.Length > 50)
            { errors.Add(new(at + "/args/pages", "A step may affect at most 50 pages.")); }
            if (step.Op == "macro.insert" && (!macroTargets.TryGetValue(step.Index, out PlanMacroTarget? target)
                || target is not { PageType: "Circuit", Empty: true, Representation: "MultiLine" }))
            { errors.Add(new(at + "/args/page", "macro.insert requires a proven empty Circuit page and a MultiLine macro.")); }
            foreach (PlanStep earlier in steps.Where(s => s.Effective && s.Index < step.Index))
            {
                if (!Overlap(earlier.ModifyScope, step.ReadScope) && !Overlap(earlier.ModifyScope, step.ModifyScope))
                { continue; }
                string[] created = earlier.Op == "page.create" ? [.. earlier.Args.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()!)] : [];
                string[] wanted = step.Op == "page.update" ? [.. step.Args.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()!)]
                    : step.Op == "macro.insert" ? [step.Args.GetProperty("page").GetString()!] : [];
                if (!step.DependsOn.Contains(earlier.Index) || wanted.Length == 0 || wanted.Any(p => created.Count(c => c == p) != 1))
                { errors.Add(new(at + "/args", "Read/modify overlap has no predictor for " + earlier.Op + " -> " + step.Op + ".")); }
            }
        }
        return [.. errors];
    }

    private static bool Overlap(PlanScope left, PlanScope right) => !left.Locators.IsEmpty
        ? left.Locators.Any(l => PlanScopeRules.Contains(right, l))
        : !right.Locators.IsEmpty ? right.Locators.Any(l => PlanScopeRules.Contains(left, l))
        : left.Pages.Intersect(right.Pages, StringComparer.Ordinal).Any();
}
