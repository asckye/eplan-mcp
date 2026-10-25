using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanNameScreenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Digests_MatchOnlyTheRelevantWindow(bool between)
    {
        var left = new PlanEvidence(PlanScopeTestData.Baseline().Snapshot)
        {
            NamesHash = "before",
            ExpectedNamesHash = "expected",
        };
        var right = left with { NamesHash = between ? "before" : "expected" };
        Assert.False(PlanNameScreen.Mismatch(left, right, between));
        Assert.True(PlanNameScreen.Mismatch(left, right with { NamesHash = "unexpected" }, between));
    }

    [Theory]
    [InlineData("+OUT/1", false, "outside_scope", "unknown")]
    [InlineData("+NEW/1", false, "unplanned_in_scope", "unknown")]
    [InlineData("+OUT/1", true, "external_between_steps", "external")]
    [InlineData("+NEW/1", true, "external_between_steps", "external")]
    public void ReenumeratedNames_ClassifyByWindowThenScope(string name, bool between, string classification, string attribution)
    {
        PlanStep step = PlanLocalEvidenceTests.Step("page.create", [], ["+NEW/1"]);
        var left = new PlanEvidence(PlanScopeTestData.Baseline().Snapshot) { Names = [], ExpectedNames = [] };
        var right = left with { Names = [new("page", name)] };
        PlanDiffEntry change = Assert.Single(PlanNameScreen.Compare(step, left, right, right.Names, between));
        Assert.Equal(classification, change.Class);
        Assert.Equal(attribution, change.Attribution);
        Assert.Equal("names", change.Property);
        Assert.Equal(name, change.Locator.GetProperty("name").GetString());
        Assert.Equal("absent", change.Before);
        Assert.Equal("occupied", change.After);
        Assert.Equal(!between, change.Status == "failed");
    }

    [Fact]
    public void Reenumeration_ReportsRemovedNamesAndRetainsTransientAlerts()
    {
        PlanStep step = PlanLocalEvidenceTests.Step("page.update", [], []);
        var left = new PlanEvidence(PlanScopeTestData.Baseline().Snapshot)
        {
            ExpectedNames = [new("function", "+REMOVED")],
        };
        var right = left with { Names = [new("page", "+TRANSIENT/1")] };
        var changes = PlanNameScreen.Compare(step, left, right, left.ExpectedNames, false);
        Assert.Equal(2, changes.Length);
        Assert.All(changes, change => Assert.Equal("outside_scope", change.Class));
    }

    [Fact]
    public void Rename_OldNameStillOccupiedOutsideObservedClosure_IsOutsideScope()
    {
        PlanBaseline baseline = PlanVerificationTestData.Baseline();
        PlanStep step = PlanVerificationTestData.Compile("device.update");
        var before = new PlanEvidence(baseline.Snapshot)
        {
            Names = [new("function", "+A1-K1")],
            ExpectedNames = [new("function", "+A1-K9")],
        };
        var after = new PlanEvidence(PlanVerificationTestData.After(step.Op, baseline.Snapshot))
        {
            Names = [new("function", "+A1-K1"), new("function", "+A1-K9")],
        };
        PlanDiffEntry change = Assert.Single(PlanNameScreen.Compare(step, before, after, after.Names, false));
        Assert.Equal("outside_scope", change.Class);
        Assert.Equal("+A1-K1", change.Locator.GetProperty("name").GetString());
    }

    [Fact]
    public void Rename_UnapprovedNameOnExplicitIdentity_IsUnplannedInScope()
    {
        PlanBaseline baseline = PlanVerificationTestData.Baseline();
        PlanStep step = PlanVerificationTestData.Compile("device.update");
        var before = new PlanEvidence(baseline.Snapshot)
        {
            Names = [new("function", "+A1-K1")],
            ExpectedNames = [new("function", "+A1-K9")],
        };
        var after = new PlanEvidence(PlanVerificationTestData.Copy(baseline.Snapshot, pages =>
        {
            foreach (SnapshotObject obj in pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K1"))
            {
                obj.Name = "+A1-K99";
            }
        }))
        { Names = [new("function", "+A1-K99")] };
        var changes = PlanNameScreen.Compare(step, before, after, after.Names, false);
        Assert.Equal(2, changes.Length);
        Assert.All(changes, change => Assert.Equal("unplanned_in_scope", change.Class));
    }

    [Fact]
    public void LocalEvidence_UnreadPageIsUnknownEvenWithWholeSnapshotMarkers()
    {
        PlanBaseline baseline = PlanVerificationTestData.Baseline();
        PlanStep step = PlanVerificationTestData.Compile("page.update");
        PlanSnapshot unread = PlanVerificationTestData.Copy(baseline.Snapshot, pages => pages.RemoveAt(0));
        PlanVerification report = PlanVerifier.Verify(PlanVerificationTestData.Definition(step) with { CompilerVersion = "2" },
            new(baseline.Snapshot), [new(step.Index, ExecutionState.Committed, new(baseline.Snapshot), new(unread, true))]);
        Assert.Equal("skipped", report.Verification);
        Assert.All(report.Validation, check => Assert.Equal("unknown", check.Status));
        Assert.DoesNotContain(report.Diff, change => change.Status == "failed");
    }

    [Fact]
    public void LocalEvidence_ApprovedAbsenceProvesDeletionButDoesNotCoverContent()
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline([], ["+A1/1"]);
        PlanStep step = PlanLocalEvidenceTests.Step("page.delete", ["+A1/1"], ["+A1/1"]) with
        {
            Expected = [PlanTestData.Json("""{"tag":"deleted","target":{"kind":"page","name":"+A1/1"},"value":{},"coveredProperties":["exists"]}""")],
        };
        var after = new PlanEvidence(PlanVerificationTestData.Copy(baseline.Snapshot, pages => pages.Clear(), false))
        {
            AbsentPages = ["+A1/1"],
            Names = [],
        };
        PlanVerification report = PlanVerifier.Verify(PlanVerificationTestData.Definition(step) with
        {
            CompilerVersion = "2",
            Project = baseline.Snapshot.Project,
        }, new(baseline.Snapshot) { Names = baseline.Names },
            [new(step.Index, ExecutionState.Committed, new(baseline.Snapshot), after)]);
        Assert.Equal("passed", report.Verification);
        Assert.Contains("+A1/1", Assert.Single(report.Coverage, c => c.Window == "write").UnobservedPages);
    }

    [Fact]
    public void LocalEvidence_WholeProjectMarkerDoesNotProveOrphanClosure()
    {
        PlanBaseline baseline = PlanVerificationTestData.Baseline();
        PlanStep step = PlanVerificationTestData.Compile("page.delete");
        PlanSnapshot after = PlanVerificationTestData.After(step.Op, baseline.Snapshot);
        PlanVerification report = PlanVerifier.Verify(PlanVerificationTestData.Definition(step) with { CompilerVersion = "2" },
            new(baseline.Snapshot, true), [new(step.Index, ExecutionState.Committed, new(baseline.Snapshot, true),
                new(after, true) { AbsentPages = ["+A1/1"] })]);
        Assert.Equal("skipped", report.Verification);
        Assert.All(report.Validation.Where(v => v.Check is "main.exists" or "functions.exists"), v => Assert.Equal("unknown", v.Status));
    }

    [Theory]
    [InlineData(null, "skipped")]
    [InlineData("unexpected", "failed")]
    [InlineData("expected", "passed")]
    public void LocalEvidence_NameScreenRequiresAProvenExpectedDigest(string? expectedHash, string verification)
    {
        PlanBaseline baseline = PlanVerificationTestData.Baseline();
        PlanStep step = PlanVerificationTestData.Compile("page.update");
        var before = new PlanEvidence(baseline.Snapshot) { NamesHash = "before", ExpectedNamesHash = expectedHash };
        var after = new PlanEvidence(PlanVerificationTestData.After(step.Op, baseline.Snapshot)) { NamesHash = "expected" };
        PlanVerification report = PlanVerifier.Verify(PlanVerificationTestData.Definition(step) with { CompilerVersion = "2" },
            new(baseline.Snapshot), [new(step.Index, ExecutionState.Committed, before, after)]);
        Assert.Equal(verification, report.Verification);
        Assert.Contains(report.Validation, check => check.Check == "names");
    }

    [Fact]
    public void CoverageSummary_BoundsCountAndUtf8BytesAndRetainsGaps()
    {
        var coverage = new PlanVerificationCoverage(1, "write", [.. Enumerable.Range(1, 10_000).Select(i => "+FAR/" + i)],
            7, ["graphics", "properties.outsideScope"]);
        PlanVerificationCoverage summary = PlanCoverage.Summary(coverage);
        Assert.Equal(8, summary.UnobservedPages.Length);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(summary, PlanJson.Options).Length < 1024);
        Assert.Contains("coverage.fullListInEvidence", summary.PropertyGaps);
        Assert.Equal(7, summary.SkippedConnections);
        Assert.Single(PlanCoverage.Summary(coverage with { UnobservedPages = [new string('中', 50), new string('中', 100)] }).UnobservedPages);
    }
}
