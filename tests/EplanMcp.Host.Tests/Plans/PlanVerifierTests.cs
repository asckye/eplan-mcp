using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using static EplanMcp.Host.Tests.Plans.PlanVerificationTestData;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanVerifierTests
{
    private static readonly string[] _descriptionProperty = ["values/11011/en_US"];

    [Fact]
    public void ApprovedValue_ProvenAfter_ReportsAppliedWithWriteWindowAttributionUnknown()
    {
        PlanStep step = Compile("page.update");
        PlanVerification report = Verify(step, After(step.Op, Baseline().Snapshot));
        Assert.Equal("passed", report.Verification);
        PlanDiffEntry change = Assert.Single(report.Diff);
        Assert.Equal("planned_applied", change.Class);
        Assert.Equal("unknown", change.Attribution);
        Assert.Equal("values/11011/en_US", change.Property);
        Assert.Equal("\"New\"", change.After);
        Assert.Equal(1, change.Locator.GetProperty("oid").GetInt64());
        Assert.All(report.Validation, v => Assert.Equal("passed", v.Status));
    }

    [Theory]
    [InlineData(ExecutionState.Committed, "mismatch")]
    [InlineData(ExecutionState.RolledBack, "mismatch")]
    [InlineData(ExecutionState.NotStarted, "not_executed")]
    public void RequiredValue_MissingOrNotExecuted_IsFailed(string execution, string reason)
    {
        PlanVerification report = Verify(Compile("page.update"), Baseline().Snapshot, execution);
        Assert.Equal("failed", report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "planned_missing" && d.Reason == reason);
    }

    [Fact]
    public void UnreadAfter_IsUnknownMissingRatherThanAbsent()
    {
        PlanVerification report = Verify(Compile("page.delete"), null);
        Assert.Equal("skipped", report.Verification);
        Assert.All(report.Diff, d =>
        {
            Assert.Equal("planned_missing", d.Class);
            Assert.Equal("unknown", d.Status);
            Assert.Equal("unobserved", d.Reason);
            Assert.Null(d.After);
        });
        Assert.DoesNotContain(report.Validation, v => v.Status == "failed");
    }

    [Fact]
    public void UnexpectedProperty_InsideModifyScope_IsUnplannedWithUnknownAttribution()
    {
        PlanStep step = Compile("page.update");
        PlanSnapshot after = Copy(After(step.Op, Baseline().Snapshot), pages =>
            pages[0].Objects[0].Values.Add(new() { Id = 11016, Value = "Unexpected frame" }));
        PlanVerification report = Verify(step, after);
        Assert.Equal("failed", report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "unplanned_in_scope" && d.Property == "values/11016" && d.Attribution == "unknown");
        Assert.False(report.OutsideScope);
    }

    [Fact]
    public void UnexpectedProperty_OutsideModifyScope_ExposesStopFlag()
    {
        PlanStep step = Compile("page.update");
        PlanSnapshot after = Copy(After(step.Op, Baseline().Snapshot), pages => Description(pages[2].Objects[0], "Concurrent"));
        PlanVerification report = Verify(step, after);
        Assert.True(report.OutsideScope);
        Assert.Equal("failed", report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "outside_scope" && d.Attribution == "unknown");
        Assert.DoesNotContain(report.Diff, d => d.Class == "external_between_steps");
    }

    [Fact]
    public void MoveOutsidePages_IsOutsideScopeEvenWhenIdentityWasApproved()
    {
        PlanStep step = Compile("device.update");
        PlanSnapshot after = Copy(After(step.Op, Baseline().Snapshot), pages =>
        {
            SnapshotObject function = pages[0].Objects.Single(o => o.Kind == "function");
            pages[0].Objects.Remove(function);
            function.Page = pages[2].Name;
            pages[2].Objects.Add(function);
        });
        PlanVerification report = Verify(step, after);
        Assert.True(report.OutsideScope);
        Assert.Contains(report.Diff, d => d.Class == "outside_scope" && d.Property == "page");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BetweenSteps_RequiresExplicitNoWriteWindow(bool noWrite)
    {
        PlanStep first = Compile("page.update");
        PlanStep second = first with { Index = 2 };
        PlanSnapshot a = After(first.Op, Baseline().Snapshot);
        PlanSnapshot g = Copy(a, pages => Description(pages[2].Objects[0], "Engineer edit"));
        PlanVerification report = PlanVerifier.Verify(Definition(first, second), new(Baseline().Snapshot),
            [new(1, ExecutionState.Committed, new(Baseline().Snapshot), new(a)), new(2, ExecutionState.Committed, new(g), new(g), noWrite)]);
        if (noWrite)
        {
            PlanDiffEntry external = Assert.Single(report.Diff, d => d.Class == "external_between_steps");
            Assert.Equal("external", external.Attribution);
            Assert.Equal("no_write_sent", external.Reason);
            Assert.Equal("passed", report.Verification);
        }
        else
        {
            Assert.DoesNotContain(report.Diff, d => d.Class == "external_between_steps");
            Assert.Contains(report.Coverage, c => c.Window == "between_steps:unobserved" && !c.UnobservedPages.IsEmpty);
        }
        Assert.False(report.OutsideScope);
    }

    [Fact]
    public void SameObject_ChangesInBothWindows_AreClassifiedByTimeFirst()
    {
        PlanStep first = Compile("page.update");
        PlanStep second = first with { Index = 2 };
        PlanSnapshot a = After(first.Op, Baseline().Snapshot);
        PlanSnapshot g = Copy(a, pages => Description(pages[0].Objects[0], "Engineer"));
        PlanVerification report = PlanVerifier.Verify(Definition(first, second), new(Baseline().Snapshot),
            [new(1, ExecutionState.Committed, new(Baseline().Snapshot), new(a)), new(2, ExecutionState.Committed, new(g), new(a), true)]);
        Assert.Contains(report.Diff, d => d.Step == 2 && d.Class == "external_between_steps" && d.After == "\"Engineer\"");
        Assert.Contains(report.Diff, d => d.Step == 2 && d.Class == "planned_applied" && d.Before == "\"Engineer\"" && d.Attribution == "unknown");
    }

    [Fact]
    public void WrongProjectOrProfile_IsUnreadEvidence()
    {
        PlanStep step = Compile("page.update");
        PlanSnapshot after = After(step.Op, Baseline().Snapshot);
        foreach (PlanSnapshot invalid in new[] { after with { Project = after.Project with { Path = after.Project.Path + "-copy" } }, after with { Profile = "other" }, after with { Format = "other" } })
        {
            PlanVerification report = Verify(step, invalid);
            Assert.Equal("skipped", report.Verification);
            Assert.All(report.Validation, v => Assert.Equal("unknown", v.Status));
        }
    }

    [Fact]
    public void UnobservedPagesConnectionsAndProperties_AppearInCoverage()
    {
        PlanStep step = Compile("page.update");
        PlanSnapshot partial = Copy(After(step.Op, Baseline().Snapshot), pages => pages.RemoveRange(1, 2), whole: false) with { SkippedConnections = 7 };
        PlanVerification report = Verify(step, partial);
        PlanVerificationCoverage write = Assert.Single(report.Coverage, c => c.Window == "write");
        Assert.Equal("+A1/2|+A10/1", string.Join('|', write.UnobservedPages));
        Assert.Equal(7, write.SkippedConnections);
        Assert.Contains("connections.withoutPage", write.PropertyGaps);
        Assert.Contains("graphics", write.PropertyGaps);
        Assert.DoesNotContain(report.Diff, d => d.Property == "exists");
        Assert.Equal("passed", report.Verification);
    }

    [Fact]
    public void Aggregation_FailureWinsOverUnknownAndNeverChangesCommittedExecution()
    {
        PlanStep step = Compile("macro.insert");
        PlanVerification report = Verify(step, Copy(Baseline().Snapshot, pages => pages.RemoveAt(0)));
        Assert.Equal("failed", report.Verification);
        Assert.Contains(report.Validation, v => v.Status == "unknown");
        PlanResult execution = PlanResult.Aggregate([new(1, step.Op, true, ExecutionState.Committed)]);
        PlanResult result = report.ApplyTo(execution);
        Assert.Equal(PlanExecution.Committed, result.Execution);
        Assert.Equal(execution.Steps, result.Steps);
        Assert.Null(execution.Verification);
        Assert.Equal("failed", result.Verification);
        Assert.Equal("skipped", result.Validation!.Value.GetProperty("electrical").GetString());
        Assert.Equal("skipped", result.Validation.Value.GetProperty("drawing").GetString());
        Assert.False(result.Retryable);
    }

    [Fact]
    public void RegisteredUnsetDescription_IsKnownNullAndOtherLanguagesAreIndependent()
    {
        PlanStep step = Compile("page.update");
        JsonElement expected = JsonSerializer.SerializeToElement(new
        {
            tag = "value",
            target = step.Expected[0].GetProperty("target"),
            value = new { description = (string?)null },
            coveredProperties = _descriptionProperty,
        }, PlanJson.Options);
        PlanSnapshot after = Copy(Baseline().Snapshot, pages => Description(pages[0].Objects[0], "Andere Sprache", "de_DE"));
        PlanVerification report = Verify(step with { Expected = [expected] }, after, before: after);
        Assert.Equal("passed", report.Verification);
        Assert.Empty(report.Diff);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deletion_RequiresCoveredAbsenceIncludingCompleteChunkedEvidence(bool wholeProject)
    {
        PlanStep step = Compile("page.delete");
        PlanSnapshot before = Baseline().Snapshot;
        PlanSnapshot after = After(step.Op, before) with { SnapshotHash = null };
        PlanVerification report = PlanVerifier.Verify(Definition(step), new(before),
            [new(1, ExecutionState.Committed, new(before), new(after, wholeProject))]);
        Assert.Equal(wholeProject ? "passed" : "skipped", report.Verification);
        if (!wholeProject)
        {
            Assert.All(report.Validation, v => Assert.Equal("unknown", v.Status));
            Assert.All(report.Diff, d => Assert.Equal("planned_missing", d.Class));
            Assert.DoesNotContain(report.Diff, d => d.After == "false");
        }
    }

    [Fact]
    public void Create_WrongTypeDescriptionAndExtraProperty_ReportsMissingAndUnplannedFields()
    {
        PlanStep step = Compile("page.create");
        PlanSnapshot after = Copy(After(step.Op, Baseline().Snapshot), pages =>
        {
            SnapshotObject obj = pages.Single(p => p.Name == "+A1/3").Objects[0];
            obj.PageType = "Overview";
            Description(obj, "Wrong");
            obj.Values.Add(new() { Id = 11016, Value = "Extra frame" });
        });
        PlanVerification report = Verify(step, after);
        Assert.Equal("failed", report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "planned_missing" && d.Property == "pageType");
        Assert.Contains(report.Diff, d => d.Class == "unplanned_in_scope" && d.Property == "values/11016");
    }

    [Fact]
    public void NotExecuted_MatchingAfter_DoesNotClaimPlanApplied()
    {
        PlanStep step = Compile("page.update");
        PlanVerification report = Verify(step, After(step.Op, Baseline().Snapshot), ExecutionState.NotStarted);
        Assert.All(report.Validation, v => Assert.Equal("not_executed", v.Reason));
        Assert.DoesNotContain(report.Diff, d => d.Class == "planned_applied");
    }

    [Fact]
    public void IneffectiveStep_OnlyBeforeEvidence_VerifiesWithoutExecution()
    {
        PlanStep step = Compile("page.update") with { Effective = false, PlannedChanges = [] };
        PlanVerification report = Verify(step, null, ExecutionState.NotStarted, After(step.Op, Baseline().Snapshot));
        Assert.Equal("passed", report.Verification);
        Assert.Empty(report.Diff);
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public void Numbering_MissingObjectsAreFailedOnlyWhenTheirPagesWereObserved(string op)
    {
        PlanStep step = Compile(op);
        PlanSnapshot after = Copy(Baseline().Snapshot, pages => pages.RemoveAt(0));
        PlanVerification missing = Verify(step, after);
        Assert.Equal("failed", missing.Verification);
        Assert.Contains(missing.Validation, v => v.Check == "name" && v.Status == "failed");
        PlanVerification unread = Verify(step, after with { SnapshotHash = null });
        Assert.Equal("skipped", unread.Verification);
        Assert.Contains(unread.Validation, v => v.Status == "unknown");
        Assert.DoesNotContain(unread.Validation, v => v.Status == "failed");
    }

    [Fact]
    public void UnknownExecution_ActualValuesCanBeVerifiedWithoutSettlingTheWrite()
    {
        PlanStep step = Compile("page.update");
        PlanVerification report = Verify(step, After(step.Op, Baseline().Snapshot), ExecutionState.Unknown);
        var failure = new PlanFailure(ErrorReason.OutcomeUnknown, "Pending settlement.", false);
        PlanResult execution = PlanResult.Aggregate([new(1, step.Op, true, ExecutionState.Unknown, failure)]);
        PlanResult result = report.ApplyTo(execution);
        Assert.Equal("passed", result.Verification);
        Assert.Equal(PlanExecution.Unknown, result.Execution);
        Assert.Equal(ErrorReason.OutcomeUnknown, result.Error!.Reason);
    }

    [Fact]
    public void StableObjectId_AfterDeviceRename_StillMatchesApprovedLocator()
    {
        PlanStep step = Compile("device.update");
        PlanSnapshot before = Copy(Baseline().Snapshot, pages =>
        {
            foreach (SnapshotObject obj in pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K1"))
            {
                obj.Shown = "-K1";
            }
        });
        PlanSnapshot after = Copy(After(step.Op, before), pages =>
        {
            foreach (SnapshotObject obj in pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K9"))
            {
                obj.Shown = "-K9";
            }
        });
        PlanVerification report = Verify(step, after, before: before);
        Assert.Equal("passed", report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "planned_applied" && d.Property == "shown");
    }

    [Theory]
    [InlineData("+A1-K9", "planned_applied", "passed")]
    [InlineData("+A1-K7", "outside_scope", "failed")]
    public void DeviceRename_DerivedReferencesOutsideModifyScope_AreAppliedOnlyForTheExactSubstitution(string reference, string expected, string verification)
    {
        // A45: renaming =F+A2-KF1 rewrote connection ends, 20403 texts and a visible name that omits the location.
        PlanStep step = Compile("device.update");
        PlanSnapshot before = Copy(Baseline().Snapshot, pages =>
        {
            foreach (SnapshotObject obj in pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K1"))
            {
                obj.Shown = "=X-K1";
            }
            SnapshotPage far = pages.Single(p => p.Name == "+A10/1");
            far.Objects.Single(o => o.Name == "+A10-K2").Values.Add(new() { Id = 20403, Name = "FUNC_PLCSYMBOLICADDRESS_CALCULATED", Value = "+A1-K1:A1" });
            far.Objects.Add(new()
            {
                Kind = "connection",
                Type = 18,
                Oid = 30,
                Name = "17/13:>17/11:A1",
                Page = "+A10/1",
                End = new() { Type = 17, Oid = 11, Function = "+A1-K1", Pin = "A1" },
                Hash = "connection-30",
            });
        });
        PlanSnapshot after = Copy(After(step.Op, before), pages =>
        {
            foreach (SnapshotObject obj in pages.SelectMany(p => p.Objects).Where(o => o.Name == "+A1-K9"))
            {
                obj.Shown = "=X-K9";
            }
            SnapshotPage far = pages.Single(p => p.Name == "+A10/1");
            far.Objects.Single(o => o.Name == "+A10-K2").Values.Single(v => v.Id == 20403).Value = reference + ":A1";
            far.Objects.Single(o => o.Kind == "connection").End!.Function = reference;
        });
        PlanVerification report = Verify(step, after, before: before);
        Assert.Equal(verification, report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "planned_applied" && d.Property == "shown");
        Assert.Contains(report.Diff, d => d.Class == expected && d.Property == "end");
        Assert.Contains(report.Diff, d => d.Class == expected && d.Property == "values/20403");
        Assert.Equal(expected == "outside_scope", report.OutsideScope);
    }

    [Fact]
    public void EscapedText_ComparedAsValuesRatherThanJsonSpellings()
    {
        PlanStep step = Compile("page.update");
        JsonElement expected = PlanTestData.Json("""{"tag":"value","target":{"kind":"page","name":"+A1/1"},"value":{"description":"A\u0042"},"coveredProperties":["values/11011/en_US"]}""");
        PlanSnapshot after = Copy(Baseline().Snapshot, pages => Description(pages[0].Objects[0], "AB"));
        PlanVerification report = Verify(step with { Expected = [expected] }, after);
        Assert.Equal("passed", report.Verification);
    }

    [Fact]
    public void Macro_OnlyVisiblePageProven_RemainsSkippedWithExplicitUnknownChecks()
    {
        PlanStep step = Compile("macro.insert");
        PlanVerification report = Verify(step, Baseline().Snapshot);
        Assert.Equal("skipped", report.Verification);
        Assert.Contains(report.Validation, v => v.Check == "page.exists" && v.Status == "passed");
        Assert.Contains(report.Validation, v => v.Check == "macro.graphics" && v.Status == "unknown");
        Assert.Contains(report.Validation, v => v.Check == "macro.placeholders" && v.Status == "unknown");
        Assert.Contains(report.Coverage, c => c.PropertyGaps.Contains("macro.functions"));
    }

    [Fact]
    public void Numbering_RemovedBeforeStep_DoesNotSilentlyReplaceTheApprovedBaseline()
    {
        PlanStep step = Compile("device.renumber");
        PlanSnapshot actual = Copy(Baseline().Snapshot, pages => pages[0].Objects.RemoveAll(o => o.Kind == "function"));
        PlanVerification report = Verify(step, actual, before: actual);
        Assert.Equal("failed", report.Verification);
        Assert.Contains(report.Diff, d => d.Class == "planned_missing" && d.Locator.GetProperty("oid").GetInt64() == 11);
    }
}
