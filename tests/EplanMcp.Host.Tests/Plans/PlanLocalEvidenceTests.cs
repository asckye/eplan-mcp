using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanLocalEvidenceTests
{
    [Fact]
    public void ReferenceKeys_UseTheStepAndObservationWindow()
    {
        Assert.Equal("1:before", PlanLocalEvidenceReader.ReferenceKey(1, "before"));
        Assert.Equal("20:after", PlanLocalEvidenceReader.ReferenceKey(20, "after"));
        Assert.Equal("1:before:names", PlanLocalEvidenceReader.ReferenceKey(1, "before") + ":names");
    }

    [Fact]
    public void Pages_FiftyOneObservedPages_RefusesTheScope()
    {
        PlanStep step = Step("page.create", [], [.. Enumerable.Range(1, 51).Select(i => "+NEW/" + i)]);
        Assert.Equal(ErrorReason.CapabilityMissing, Assert.Throws<Host.Bridge.BridgeCallException>(() =>
            PlanLocalEvidenceReader.Pages(step, PlanScopeTestData.Baseline().Snapshot)).Error.Reason);
    }

    [Fact]
    public void Pages_UsesSameNameClosureAndResolvedConnectionEnds_WithoutRecursiveExpansion()
    {
        SnapshotObject connection = new()
        {
            Kind = "connection",
            Type = 25,
            Oid = 100,
            Page = "+A1/1",
            Hash = "connection",
            End = new() { Type = 17, Oid = 13 },
        };
        SnapshotObject remote = PlanScopeTestData.Function(14, "+REMOTE/1", "+OTHER");
        SnapshotObject secondHop = new()
        {
            Kind = "connection",
            Type = 25,
            Oid = 101,
            Page = "+A1/2",
            Hash = "second-hop",
            End = new() { Type = 17, Oid = remote.Oid },
        };
        PlanBaseline baseline = PlanScopeTestData.Baseline([PlanScopeTestData.Function(11, "+A1/1", main: true),
            PlanScopeTestData.Function(12, "+A1/2"), PlanScopeTestData.Function(13, "+A10/1", "+A10-K2"), remote, connection, secondHop],
            ["+A1/1", "+A1/2", "+A10/1", "+REMOTE/1"]);
        PlanStep step = Step("page.update", ["+A1/1"], []);

        Assert.Equal(["+A1/1", "+A1/2", "+A10/1"], PlanLocalEvidenceReader.Pages(step, baseline.Snapshot));
    }

    [Fact]
    public void Pages_ProjectsLocatorsOrphansAndNewPages_WithoutIncludingUnrelatedBaselinePages()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanLocator remaining = PlanScopeRules.Locator(baseline.Snapshot.Project.Path,
            baseline.Snapshot.Pages[1].Objects.Single(o => o.Kind == "function"));
        PlanStep step = Step("page.delete", [], ["+NEW/1"]) with
        {
            ModifyScope = new(["+NEW/1"], [], [remaining]),
            Expected = [JsonSerializer.SerializeToElement(new
            {
                tag = "orphaned", value = new { remaining = new[] { remaining } },
            }, PlanJson.Options)],
        };

        Assert.Equal(["+A1/1", "+A1/2", "+NEW/1"], PlanLocalEvidenceReader.Pages(step, baseline.Snapshot));
        Assert.Empty(PlanLocalEvidenceReader.Pages(Step("page.create", [], []), baseline.Snapshot));
        Assert.Equal(["+NEW/1"], PlanLocalEvidenceReader.Pages(Step("page.create", [], ["+NEW/1"]), baseline.Snapshot));
    }

    [Fact]
    public void ApprovedAbsence_CoversOnlyExplicitPages_AndAddsNoPersistedMember()
    {
        var observation = new PlanEvidence(PlanScopeTestData.Baseline().Snapshot with { SnapshotHash = null }) { AbsentPages = ["+NEW/1"] };

        Assert.True(observation.Covers("+NEW/1"));
        Assert.False(observation.Covers("+MISSING/1"));
        Assert.False(observation.Covers(null));
        JsonElement json = JsonSerializer.SerializeToElement(observation, PlanJson.Options);
        Assert.Equal(["snapshot", "wholeProject"], json.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void ExpectedNames_AppliesApprovedCreationDeletionAndCompleteRename()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        PlanStep create = Step("page.create", [], ["+NEW/1"]) with { PlannedChanges = [new("create", "page", "+NEW/1", null, null, null)] };
        Assert.Contains(new PlanOccupiedName("page", "+NEW/1"), PlanLocalEvidenceReader.ExpectedNames(create, baseline.Names, baseline.Snapshot));
        PlanStep delete = Step("page.delete", ["+A1/1"], ["+A1/1"]) with { PlannedChanges = [new("delete", "page", "+A1/1", null, null, null)] };
        PlanOccupiedName[] deleted = PlanLocalEvidenceReader.ExpectedNames(delete, baseline.Names, baseline.Snapshot);
        Assert.DoesNotContain(new PlanOccupiedName("page", "+A1/1"), deleted);
        Assert.Contains(new PlanOccupiedName("function", "+A1-K1"), deleted);
        delete = delete with { PlannedChanges = [.. delete.PlannedChanges, new("delete", "page", "+A1/2", null, null, null)] };
        Assert.DoesNotContain(new PlanOccupiedName("function", "+A1-K1"), PlanLocalEvidenceReader.ExpectedNames(delete, baseline.Names, baseline.Snapshot));
        PlanStep rename = Step("device.update", ["+A1/1", "+A1/2"], ["+A1/1", "+A1/2"]) with
        {
            PlannedChanges = [new("update", "device", "+A1-K1", "tag", "+A1-K1", "+A1-K9")],
        };
        PlanOccupiedName[] renamed = PlanLocalEvidenceReader.ExpectedNames(rename, baseline.Names, baseline.Snapshot);
        Assert.DoesNotContain(new PlanOccupiedName("function", "+A1-K1"), renamed);
        Assert.Contains(new PlanOccupiedName("function", "+A1-K9"), renamed);
        rename = rename with { ModifyScope = PlanScopeRules.Pages(["+A1/1"]) };
        Assert.Contains(new PlanOccupiedName("function", "+A1-K1"), PlanLocalEvidenceReader.ExpectedNames(rename, baseline.Names, baseline.Snapshot));
    }

    internal static PlanStep Step(string op, string[] read, string[] modify) => new(1, op, PlanTestData.Json("{}"), ToolTier.Write,
        PlanScopeRules.Pages(read), PlanScopeRules.Pages(modify), [], [], [], [], null, true);
}
