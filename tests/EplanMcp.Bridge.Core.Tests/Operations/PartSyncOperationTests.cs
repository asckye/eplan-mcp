using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// part.sync.plan and part.sync.execute (docs/tools/eplan_part_sync.md): the parts the project stores classified by the
/// mode against the configured database, the referenced parts the project will store (S24 r15), a plan that names the
/// project and the database, the project backup before the synchronization in its transaction, OUTCOME_UNKNOWN once it
/// began, and the read-back of both. Real backup folders; the project and the database are fakes.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PartSyncOperationTests : IDisposable
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-partsync-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakePartReader _reader = new();
    private readonly FakePartsDatabaseWriter _writer = new();
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public PartSyncOperationTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = "=A/1" }, new PageSummary { Name = "=A/2" }]);
        _backup = new FakeProjectBackup(_pages);

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

    public void Dispose() => Directory.Delete(_folder, recursive: true);

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
        Assert.Empty(_writer.Syncs);
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
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Run<WritePlan>(new PartSyncPlanOperation(new FakeCatalog(_project), _reader, _writer), new PartSyncArgs { Mode = "mirror" })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Equal(2, error.Metadata!["violations"].Split('\n').Length);
    }

    [Fact]
    public void TheExecution_BacksTheProjectUp_SynchronizesInATransaction_AndReadsBothBack()
    {
        WritePlan plan = Plan(null);
        _writer.OnSync = () =>
        {
            _writer.Parts["NEW.PART"] = 2;
            _reader.Parts.Articles.Add(new ArticleRecord { PartNumber = "PXC.3209510", Variant = "1" });
        };

        PartSyncData data = Execute(plan.Fingerprint, null);

        Assert.Equal([(_project.Path, Path.Combine(_folder, "Line1.elk"))], _backup.Copies);
        Assert.Equal([(_project.Path, "append_new")], _writer.Syncs);
        Assert.Equal(["Begin AI: eplan_part_sync", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(["NEW.PART"], data.Added);
        Assert.Equal(["PXC.3209510"], data.StoredInProject);
        Assert.Equal(1, data.AddedCount);
        Assert.Equal((3, 5, 5), (data.PartsBefore, data.PartsAfter, data.PartsExpected));
        Assert.Equal((3, 4), (data.ProjectPartsBefore, data.ProjectPartsAfter));
        Assert.Empty(data.Missing);
        Assert.Empty(data.NotStored);
    }

    [Fact]
    public void AReadBackThatDoesNotMatch_IsReported_NotAnError()
    {
        WritePlan plan = Plan(null);

        PartSyncData data = Execute(plan.Fingerprint, null);

        Assert.Equal(["NEW.PART"], data.Missing);
        Assert.Equal(["PXC.3209510"], data.NotStored);
    }

    [Fact]
    public void ADatabaseThatGainedOneOfTheParts_IsStale_AndNothingIsBackedUpOrSynchronized()
    {
        WritePlan plan = Plan(null);
        _writer.Parts["NEW.PART"] = 1;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(plan.Fingerprint, null)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Copies);
        Assert.Empty(_writer.Syncs);
    }

    [Fact]
    public void WithoutABackup_NothingIsSynchronized()
    {
        WritePlan plan = Plan(null);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(plan.Fingerprint, new BackupTarget { Path = Path.Combine(_folder, "gone", "Line1.elk"), MaxSeconds = 150 })).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_writer.Syncs);
    }

    [Fact]
    public void ASynchronizationThatFails_RollsTheProjectBack_ButItsOutcomeIsUnknown()
    {
        WritePlan plan = Plan(null);
        _writer.SyncFailure = new InvalidOperationException("S063110 no LockingStep");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(plan.Fingerprint, null)).Error;

        Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown, PartSyncExecuteOperation.UnknownMessage), (error.Reason, error.Execution, error.Message));
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    private static T Run<T>(IOperation operation, PartSyncArgs args) => (T)operation.Execute(args)!;

    private WritePlan Plan(string? mode) =>
        Run<WritePlan>(new PartSyncPlanOperation(new FakeCatalog(_project), _reader, _writer), new PartSyncArgs { Project = "Line1", Mode = mode });

    private PartSyncData Execute(string fingerprint, BackupTarget? backup) => Run<PartSyncData>(
        new PartSyncExecuteOperation(new FakeCatalog(_project), _pages, _reader, _writer, _backup, _transactions),
        new PartSyncArgs { Project = "Line1", Fingerprint = fingerprint, Backup = backup ?? new BackupTarget { Path = Path.Combine(_folder, "Line1.elk"), MaxSeconds = 150 } });
}
