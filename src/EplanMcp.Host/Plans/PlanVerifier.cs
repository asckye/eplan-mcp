using System.Text.Json;
using EplanMcp.Contracts;
using static EplanMcp.Host.Plans.PlanVerificationFacts;

namespace EplanMcp.Host.Plans;

/// <summary>Pure evidence comparison. Verification never revises a Bridge execution fact.</summary>
internal static class PlanVerifier
{
    public static PlanVerification Verify(PlanDefinition plan, PlanEvidence baseline, IReadOnlyList<PlanStepEvidence> evidence)
        => new Verification(plan, baseline, evidence).Run();

    /// <summary>One verification: the steps in order, each adding its checks, its differences and what it could not observe.</summary>
    private sealed class Verification(PlanDefinition plan, PlanEvidence baseline, IReadOnlyList<PlanStepEvidence> evidence)
    {
        private readonly List<PlanValidation> _validation = [];
        private readonly List<PlanDiffEntry> _diff = [];
        private readonly List<PlanVerificationCoverage> _coverage = [];
        private readonly bool _local = PlanApplyChecks.UsesLocalBaseline(plan.CompilerVersion);

        public PlanVerification Run()
        {
            if (Compatible(baseline) is null)
            {
                throw new ArgumentException("Baseline must be standard evidence for the compiled project.", nameof(baseline));
            }
            PlanEvidence? previous = null;
            foreach (PlanStep step in plan.Steps)
            { previous = VerifyStep(step, previous); }
            string verification = _validation.Any(v => v.Status == "failed") || _diff.Any(d => d.Status == "failed") ? "failed"
                : _validation.Count > 0 && _validation.All(v => v.Status == "passed") ? "passed" : "skipped";
            return new(verification, [.. _validation], [.. _diff], [.. _coverage]);
        }

        /// <summary>Returns the step's evidence after its write: what the next step compares its own start with.</summary>
        private PlanEvidence? VerifyStep(PlanStep step, PlanEvidence? previous)
        {
            PlanStepEvidence? input = evidence.SingleOrDefault(e => e.Index == step.Index);
            if (_local && input is not null)
            {
                _diff.AddRange(input.NameDiff);
            }
            PlanEvidence? before = Compatible(input?.Before), after = Compatible(input?.After);
            bool unsent = step.Effective && (input is null || input.Execution == ExecutionState.NotStarted);
            List<Fact> facts = Check(step, baseline, step.Effective ? after : after ?? before);
            if (_local && step.Effective && (before?.NamesHash is not null || after?.NamesHash is not null))
            {
                string? expected = before?.ExpectedNamesHash;
                string status = expected is null || after?.NamesHash is null ? "unknown" : expected == after.NamesHash ? "passed" : "failed";
                facts.Add(new(JsonSerializer.SerializeToElement(step.ModifyScope, PlanJson.Options), "names", expected, after?.NamesHash,
                    status, expected is null ? "expected_names_unavailable" : status == "unknown" ? "unobserved" : status == "passed" ? "proven" : "name_set_mismatch"));
            }
            if (facts.Count == 0)
            {
                facts.Add(new(JsonSerializer.SerializeToElement(step.ModifyScope, PlanJson.Options), "expected", null, null, "unknown", "no_checks"));
            }
            foreach (Fact fact in facts)
            {
                string status = unsent ? "failed" : fact.Status;
                string reason = unsent ? "not_executed" : fact.Reason;
                _validation.Add(new(step.Index, fact.Locator, fact.Property, status, reason));
                if (status != "passed")
                {
                    _diff.Add(new(step.Index, "planned_missing", fact.Locator, fact.Property, status, reason, "unknown", null, fact.Actual, fact.Expected));
                }
            }
            if (!unsent)
            {
                Compare(step, before, after, false, facts);
            }
            if (previous is not null || step != plan.Steps[0])
            {
                bool noWrite = input?.NoWriteSincePrevious == true;
                if (noWrite)
                {
                    Compare(step, previous, before, true, []);
                }
                Cover(step, previous, before, noWrite ? "between_steps" : "between_steps:unobserved", !noWrite);
            }
            else
            {
                Cover(step, baseline, before, "baseline_to_first_step:unobserved", true);
            }
            Cover(step, before, after, "write", false);
            return after;
        }

        private PlanEvidence? Compatible(PlanEvidence? observation) => observation is not null
            && Path.IsPathFullyQualified(observation.Snapshot.Project.Path)
            && PlanSnapshotHashes.ProjectKey(observation.Snapshot.Project.Path) == PlanSnapshotHashes.ProjectKey(plan.Project.Path)
            && observation.Snapshot.Profile == SnapshotProfiles.StandardName && observation.Snapshot.Format == SnapshotProfiles.Format
            && (plan.Project.Guid is null || observation.Snapshot.Project.Guid == plan.Project.Guid)
                ? _local ? observation with { WholeProject = false, Snapshot = observation.Snapshot with { SnapshotHash = null } } : observation : null;

        private void Cover(PlanStep step, PlanEvidence? left, PlanEvidence? right, string window, bool unobserved)
        {
            string[] pages = [.. baseline.Snapshot.Pages.Select(p => p.Name).Concat(step.ReadScope.Pages).Concat(step.ModifyScope.Pages)
                .Concat(left?.Snapshot.Pages.Select(p => p.Name) ?? []).Concat(right?.Snapshot.Pages.Select(p => p.Name) ?? []).Distinct(StringComparer.Ordinal)];
            string[] gaps = ["graphics", "texts", "placeholders", "connections.withoutPage", .. _validation.Where(v => v.Step == step.Index && v.Status == "unknown").Select(v => v.Check),
                .. step.Coverage is { } c && c.TryGetProperty("unknown", out JsonElement unknown) ? unknown.EnumerateArray().Select(p => p.GetString()!) : []];
            if (_local)
            {
                pages = [.. pages.Concat(baseline.Names.Concat(left?.Names ?? []).Concat(right?.Names ?? []).Where(n => n.Kind == "page").Select(n => n.Name))
                    .Distinct(StringComparer.Ordinal)];
                gaps = [.. gaps, "properties.outsideScope", "functions.sameNameInstancesOutsideClosure", "objects.withoutPage.unreadProperties", "connections.3d"];
                if (unobserved)
                {
                    gaps = [.. gaps, "interval.unobserved"];
                }
            }
            _coverage.Add(new(step.Index, window, [.. pages.Where(p => unobserved || left?.Covers(p) != true || right?.Covers(p) != true).Order(StringComparer.Ordinal)],
                Math.Max(left?.Snapshot.SkippedConnections ?? 0, right?.Snapshot.SkippedConnections ?? 0), [.. gaps.Distinct(StringComparer.Ordinal)]));
            if (_local)
            {
                _coverage[^1] = _coverage[^1] with
                {
                    UnobservedPages = [.. pages.Where(p => unobserved || left?.Snapshot.Pages.Any(a => a.Name == p) != true
                        || right?.Snapshot.Pages.Any(a => a.Name == p) != true).Order(StringComparer.Ordinal)],
                };
            }
        }

        private void Compare(PlanStep step, PlanEvidence? left, PlanEvidence? right, bool between, List<Fact> facts)
        {
            Dictionary<string, SnapshotObject> old = Objects(left), current = Objects(right);
            foreach (string key in old.Keys.Union(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                SnapshotObject? a = old.GetValueOrDefault(key), b = current.GetValueOrDefault(key);
                SnapshotObject obj = a ?? b!;
                string? page = obj.Kind == "page" ? obj.Name : obj.Page;
                if (a is null && left?.Covers(page) != true || b is null && right?.Covers(page) != true)
                {
                    continue;
                }
                var locator = PlanScopeRules.Locator(plan.Project.Path, obj);
                JsonElement target = JsonSerializer.SerializeToElement(locator, PlanJson.Options);
                Dictionary<string, string?> first = a is null ? new(StringComparer.Ordinal) : Properties(a);
                Dictionary<string, string?> last = b is null ? new(StringComparer.Ordinal) : Properties(b);
                var change = new ObjectChange(step, a, b, obj, facts);
                string[] properties = b is null ? ["exists"] : a is null ? ["exists", .. last.Keys] : [.. first.Keys.Union(last.Keys, StringComparer.Ordinal)];
                foreach (string property in properties)
                {
                    string? from = property == "exists" ? a is null ? "false" : "true" : first.GetValueOrDefault(property);
                    string? to = property == "exists" ? b is null ? "false" : "true" : last.GetValueOrDefault(property);
                    if (from == to)
                    {
                        continue;
                    }
                    bool inside = change.Inside(locator);
                    bool allowed = change.Planned(property, from, to);
                    // References to the renamed functions change with them, also on objects outside the modify scope.
                    if (!between && PlanDerivedRename.Reference(step, property, from, to))
                    {
                        inside = true;
                        allowed = true;
                    }
                    allowed |= change.Derived(property);
                    string classification = between ? "external_between_steps" : !inside ? "outside_scope" : allowed ? "planned_applied" : "unplanned_in_scope";
                    bool unknown = classification == "unplanned_in_scope" && step.Op == "macro.insert" && a is null
                        && facts.Any(f => f.Property == "macro.functions" && f.Status == "unknown");
                    _diff.Add(new(step.Index, classification, target, property, unknown ? "unknown" : between || classification == "planned_applied" ? "passed" : "failed",
                        unknown ? "approval_inventory_unobserved" : between ? "no_write_sent" : "observed_change", between ? "external" : "unknown", from, to));
                }
            }
        }
    }

    /// <summary>One object before and after a step: whether a changed property lies in the step's scope and was planned.</summary>
    private sealed class ObjectChange(PlanStep step, SnapshotObject? a, SnapshotObject? b, SnapshotObject obj, List<Fact> facts)
    {
        public bool Inside(PlanLocator locator)
        {
            bool inside = PlanScopeRules.Contains(step.ModifyScope, locator);
            if (b is not null && a?.Page != b.Page && step.Op != "page.renumber")
            {
                inside &= (step.ModifyScope.Pages.IsEmpty || step.ModifyScope.Pages.Contains(b.Page!, StringComparer.Ordinal))
                    && (step.ModifyScope.Structures.IsEmpty || step.ModifyScope.Structures.Any(s => PlanScopeRules.StructurePrefix(b.Page ?? "", s)));
            }
            return inside;
        }

        public bool Planned(string property, string? from, string? to)
        {
            bool allowed = facts.Any(f => Targets(f.Locator, obj) && f.Property == property && f.Status == "passed" && f.Actual == to);
            allowed |= a is null && property == "name" && facts.Any(f => Targets(f.Locator, obj) && f.Property == "exists" && f.Status == "passed");
            allowed |= step.Op == "device.update" && property == "shown" && (a?.Shown is { Length: > 0 } && b?.Shown is { Length: > 0 }
                && a.Name?.EndsWith(a.Shown, StringComparison.Ordinal) == true && b.Name?.EndsWith(b.Shown, StringComparison.Ordinal) == true
                || PlanDerivedRename.Visible(step, from, to))
                && facts.Any(f => Targets(f.Locator, obj) && f.Property == "name" && f.Status == "passed");
            return allowed;
        }

        /// <summary>What a renumbering or a macro insertion changes without naming it in the plan.</summary>
        public bool Derived(string property)
        {
            bool allowed = false;
            if (step.Op is "page.renumber" or "device.renumber")
            {
                allowed |= a is not null && b is not null && (property == "name" && (step.Op == "page.renumber" ? obj.Kind == "page" : obj.Kind is not ("page" or "connection"))
                    || step.Op == "page.renumber" && property == "page" || step.Op == "device.renumber" && property == "shown");
            }
            if (step.Op == "macro.insert" && a is null)
            {
                allowed |= facts.Any(f => f.Property == "macro.functions" && f.Status == "passed") && b!.Page == step.Args.GetProperty("page").GetString();
            }
            return allowed;
        }
    }
}
