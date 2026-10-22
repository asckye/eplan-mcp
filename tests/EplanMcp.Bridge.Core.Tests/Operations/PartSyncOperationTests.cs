using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// part.sync.plan (docs/tools/eplan_part_sync.md): the parts the project stores classified by the mode against the
/// configured database, the referenced parts the project will store (S24 r15), and a plan that names the project and
/// the database. The project and the database are fakes.
/// </summary>
[Trait("Size", "Small")]
public sealed class PartSyncOperationTests
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly FakePartReader _reader = new();
    private readonly FakePartsDatabaseWriter _writer = new();

    public PartSyncOperationTests()
    {
        // Stored: one part the database has, one it lacks (two variants). Referenced only: one the database has, one it lacks.
        _reader.Parts.Articles.AddRange(
        [
            new ArticleRecord { PartNumber = "SIE.3RH2122-1HB40", Variant = "1" },
            new ArticleRecord { PartNumber = "NEW.PART", Variant = "1" },
            new ArticleRecord { PartNumber = "NEW.PART", Variant = "2" },
        ]);
        _reader.Parts.References.AddRange(
        [
            new PartReferenceRecord { PartNumber = "SIE.3RH2122-1HB40", Count = 1 },
            new PartReferenceRecord { PartNumber = "PXC.3209510", Count = 4 },
            new PartReferenceRecord { PartNumber = "GONE.PART", Count = 1 },
        ]);
    }

    [Theory]
    [InlineData(null, "NEW.PART", "")]
    [InlineData("overwrite_existing", "", "SIE.3RH2122-1HB40")]
    [InlineData("overwrite_and_append", "NEW.PART", "SIE.3RH2122-1HB40")]
    public void ThePlan_ClassifiesTheStoredPartsByTheMode_AndStoresTheReferencedOnes(string? mode, string added, string overwritten)
    {
        WritePlan plan = Plan(mode);

        Assert.Equal(("Line1", _project.Path, @"$(MD_PARTS)\EPLAN_parts.alk"), (plan.Project, plan.ProjectPath, plan.MasterData!.Database));
        Assert.Equal(added, plan.Details![PartImportPlanOperation.Added]);
        Assert.Equal(overwritten, plan.Details[PartImportPlanOperation.Updated]);
        Assert.Equal(("PXC.3209510", "1", "3", "3"), (plan.Details[PartSyncPlanOperation.Stored], plan.Details[PartSyncPlanOperation.StoredCount], plan.Details[PartImportPlanOperation.PartsBefore], plan.Details[PartSyncPlanOperation.ProjectPartsBefore]));
        Assert.Equal(
            [
                .. added.Length > 0 ? new[] { ("create", "parts", (string?)null, "1") } : [],
                .. overwritten.Length > 0 ? new[] { ("update", "parts", (string?)null, "1") } : [],
                ("update", "project", "3", "1"),
            ],
            plan.Changes.Select(c => (c.Action, c.Kind, c.OldValue, c.NewValue!)));
        Assert.All(plan.Changes, c => Assert.Equal("Line1", c.Name));
    }

    [Fact]
    public void AProjectWhoseSynchronizationChangesNothing_PlansNoChange()
    {
        _reader.Parts.Articles.RemoveAll(a => a.PartNumber == "NEW.PART");
        _reader.Parts.References.RemoveAll(r => r.PartNumber == "PXC.3209510");

        Assert.Empty(Plan(null).Changes);
    }

    [Fact]
    public void ADatabaseOnSqlServer_IsRefusedByPolicy()
    {
        _writer.Database = new ConfiguredDatabase { Database = "SRV01|EPLAN_Parts|0" };

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Plan(null)).Error;

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void EveryArgumentProblem_IsReportedAtOnce()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => PlanWith(new PartSyncArgs { Mode = "mirror" })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(2, error.Metadata!["violations"].Split('\n').Length);
    }

    private WritePlan Plan(string? mode) => PlanWith(new PartSyncArgs { Project = "Line1", Mode = mode });

    private WritePlan PlanWith(PartSyncArgs args) => (WritePlan)new PartSyncPlanOperation(new FakeCatalog(_project), _reader, _writer).Execute(args)!;
}

