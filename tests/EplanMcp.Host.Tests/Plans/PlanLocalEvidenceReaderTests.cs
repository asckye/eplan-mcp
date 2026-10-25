using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Tools;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanLocalEvidenceReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LocalObservation_FollowsChunkContinuation_WithoutExpandingE()
    {
        using var fixture = new PlanStoreFixture();
        await fixture.Publish();
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        FakeBridgeClient bridge = Bridge(baseline, 1);
        PlanStep step = PlanLocalEvidenceTests.Step("page.update", ["+A1/1", "+A1/2"], []);
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        PlanEvidence evidence = await new PlanLocalEvidenceReader(bridge, fixture.Store).ReadAsync(
            fixture.Definition with { Project = baseline.Snapshot.Project }, step, "before", ["+A1/1", "+A1/2"], null, references, Token);

        Assert.False(evidence.WholeProject);
        Assert.Equal(["+A1/1", "+A1/2"], evidence.Snapshot.Pages.Select(p => p.Name));
        Assert.Equal(2, bridge.Calls.Count(c => c.Operation == OperationNames.ProjectSnapshot));
        Assert.All(bridge.Calls.Where(c => c.Args is ProjectSnapshotArgs), c =>
            Assert.Equal(["+A1/1", "+A1/2"], Assert.IsType<ProjectSnapshotArgs>(c.Args).Pages));
        Assert.Equal(1, Assert.IsType<ProjectSnapshotArgs>(bridge.Calls.Last().Args).AfterPage);
        Assert.Equal(4, references.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalObservation_RejectsIncompleteNamesAndUnexpectedMissingPages(bool missingPage)
    {
        using var fixture = new PlanStoreFixture();
        await fixture.Publish();
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        FakeBridgeClient bridge = Bridge(baseline);
        if (!missingPage)
        {
            bridge.Handlers[OperationNames.PlanNames] = _ => new PlanNamesData { Complete = false };
        }
        PlanStep step = PlanLocalEvidenceTests.Step("page.update", ["+NOT/1"], []);
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanLocalEvidenceReader(bridge, fixture.Store).ReadAsync(
            fixture.Definition with { Project = baseline.Snapshot.Project }, step, "before", ["+NOT/1"], null,
            new Dictionary<string, string>(), Token));

        Assert.Equal(missingPage ? ErrorReason.PlanStale : ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.DoesNotContain(bridge.Calls, c => c.Operation == OperationNames.ProjectSnapshot);
    }

    [Fact]
    public async Task LocalObservation_RejectsActualDensePageOverTenThousandObjects()
    {
        using var fixture = new PlanStoreFixture();
        await fixture.Publish();
        PlanBaseline baseline = PlanScopeTestData.Baseline(Enumerable.Range(1, 10_000).Select(i =>
            PlanScopeTestData.Function(i, "+DENSE/1", "+DENSE-K" + i)), ["+DENSE/1"]);
        FakeBridgeClient bridge = Bridge(baseline);
        PlanStep step = PlanLocalEvidenceTests.Step("page.update", ["+DENSE/1"], []);
        var references = new Dictionary<string, string>();
        BridgeCallException error = await Assert.ThrowsAsync<BridgeCallException>(() => new PlanLocalEvidenceReader(bridge, fixture.Store).ReadAsync(
            fixture.Definition with { Project = baseline.Snapshot.Project }, step, "before", ["+DENSE/1"], null, references, Token));

        Assert.Equal(ErrorReason.CapabilityMissing, error.Error.Reason);
        Assert.Equal("plan.evidence:50_pages_10000_objects", error.Error.Metadata!["missing"]);
        Assert.DoesNotContain("1:before", references.Keys);
    }

    [Fact]
    public async Task PageRenumber_AfterEvidenceBindsOriginalPageOids_ToFreshNames()
    {
        using var fixture = new PlanStoreFixture();
        await fixture.Publish();
        PlanBaseline baseline = PlanScopeTestData.Baseline([], ["+A1/1"]);
        var before = new PlanEvidence(baseline.Snapshot with { SnapshotHash = null });
        PlanSnapshot after = PlanVerificationTestData.Copy(baseline.Snapshot, pages =>
        {
            pages[0].Name = "+A1/10";
            pages[0].Objects[0].Name = "+A1/10";
        }, whole: false);
        PlanOccupiedName[] names = [new("page", "+A1/10")];
        FakeBridgeClient bridge = Bridge(new(after, [.. names], PlanSnapshotHashes.Names(after.Project.Path, names)));
        bridge.Handlers[OperationNames.ObjectResolve] = args =>
        {
            ObjectLocator locator = Assert.Single(Assert.IsType<ObjectResolveArgs>(args).Locators);
            Assert.Equal(1, locator.Oid);
            Assert.Null(locator.Name);
            return new ObjectResolveData
            {
                Project = after.Project.Name,
                ProjectPath = after.Project.Path,
                Results = [new() { Status = LocatorResult.Resolved, ActualName = "+A1/10" }],
            };
        };
        PlanEvidence evidence = await new PlanLocalEvidenceReader(bridge, fixture.Store).ReadAsync(
            fixture.Definition with { Project = after.Project }, PlanLocalEvidenceTests.Step("page.renumber", ["+A1/1"], ["+A1/1"]),
            "after", ["+A1/1"], before, new Dictionary<string, string>(), Token);

        Assert.Equal("+A1/10", Assert.Single(evidence.Snapshot.Pages).Name);
        Assert.Equal(1, Assert.Single(evidence.Snapshot.Pages).Oid);
        Assert.Equal(["+A1/10"], Assert.IsType<ProjectSnapshotArgs>(bridge.Calls.Last().Args).Pages);
    }

    private static FakeBridgeClient Bridge(PlanBaseline baseline, int chunkSize = 50)
    {
        var bridge = new FakeBridgeClient();
        bridge.Status = bridge.Status with { Handshake = new() { Operations = [OperationNames.PlanNames] } };
        bridge.Handlers[OperationNames.PlanNames] = _ => new PlanNamesData
        {
            Project = baseline.Snapshot.Project.Name,
            ProjectPath = baseline.Snapshot.Project.Path,
            ProjectGuid = baseline.Snapshot.Project.Guid,
            Complete = true,
            NamesHash = baseline.NamesHash,
            Names = [.. baseline.Names.Select(n => new PlanNameEntry { Kind = n.Kind, Name = n.Name })],
        };
        bridge.Handlers[OperationNames.ProjectSnapshot] = args =>
        {
            ProjectSnapshotArgs request = Assert.IsType<ProjectSnapshotArgs>(args);
            Assert.NotNull(request.Pages);
            SnapshotPage[] wanted = [.. baseline.Snapshot.Pages.Where(p => request.Pages.Contains(p.Name, StringComparer.Ordinal)).OrderBy(p => p.Oid)];
            SnapshotPage[] pages = [.. wanted.Where(p => p.Oid > (request.AfterPage ?? 0)).Take(chunkSize)];
            bool complete = pages.Length == 0 || pages[^1].Oid == wanted[^1].Oid;
            return new ProjectSnapshotData
            {
                Project = baseline.Snapshot.Project.Name,
                ProjectPath = baseline.Snapshot.Project.Path,
                ProjectGuid = baseline.Snapshot.Project.Guid,
                Profile = SnapshotProfiles.StandardName,
                Format = SnapshotProfiles.Format,
                Pages = [.. pages],
                PageCount = wanted.Length,
                Complete = complete,
                NextAfterPage = complete ? null : pages[^1].Oid,
            };
        };
        return bridge;
    }
}
