using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// page.delete.execute against fakes that remove pages the way spike S12 found EPLAN does (api-notes A18): the
/// fingerprint again, the backup before the transaction, the removal in one transaction and the read-back. Medium: the
/// backup goes into a real temporary folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PageDeleteExecuteTests : IDisposable
{
    private const string Page1 = "=GAA+A1&EFS1/1";
    private const string Page2 = "=GAA+A1&EFS1/2";
    private const string Page3 = "=GAA+A1&EFS1/3";

    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-delete-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakePageRemover _remover;
    private readonly FakeProjectBackup _backup;
    private readonly FakeTransactions _transactions = new();

    public PageDeleteExecuteTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = Page1, Type = "Circuit" }, new PageSummary { Name = Page2, Type = "Circuit" }, new PageSummary { Name = Page3, Type = "Circuit" }]);
        _remover = new FakePageRemover(_pages);
        _backup = new FakeProjectBackup(_pages);
        _remover.Placed.AddRange(
        [
            Function("+A1-WE1", main: true, Page1, "c1"), // a cable: its cores stay on page 2 (S12)
            Function("+A1-WE1", main: false, Page2, "c2"),
            Function("+A1-WE1", main: false, Page2, "c3"),
            Function("=GAA+A1-MA1", main: true, Page1, "m1"),
            Function("=GAA+A1-MA1", main: false, Page2, "m2"),
            Function("=GAA+A1-MA1", main: true, Page3, "m3"),
            Function("=GAA+A1-KF1", main: true, Page3, "k1"),
        ]);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void BacksUpFirst_ThenDeletesInOneTransaction_AndReadsBack()
    {
        string fingerprint = Plan(Page1, Page3).Fingerprint;
        _transactions.Snapshot = () =>
        {
            Assert.Single(_backup.Copies); // the copy exists before the transaction begins
            return () => { };
        };

        PageDeleteData data = Execute(fingerprint, Page1, Page3);

        Assert.Equal([Page1, Page3], _remover.Removed);
        Assert.Equal([Page2], _pages.Pages.Select(p => p.Name));
        Assert.Equal(["Begin AI: eplan_page_delete", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal([(Page1, "Circuit", 2), (Page3, "Circuit", 2)], data.Pages.Select(p => (p.Name, p.Type, p.Functions)));
        Assert.Equal(["+A1-WE1", "=GAA+A1-MA1"], data.Orphaned.Select(o => o.Tag)); // MA1 lost its main function on page 3 too
        Assert.Equal((2, 2, 1), (data.OrphanedCount, data.Orphaned[0].FunctionsLeft, data.Orphaned[1].FunctionsLeft));
        Assert.Equal((Path.Combine(_folder, "Line1.elk"), "Line1"), (Assert.Single(_backup.Copies).Copy, data.Project));
    }

    [Fact]
    public void WithoutAFingerprint_IsNotStarted()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(null, Page1)).Error;

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Copies);
    }

    [Fact]
    public void WhenWhatTheDevicesKeepChanged_IsStaleAndNothingIsBackedUp()
    {
        string fingerprint = Plan(Page1).Fingerprint;
        _remover.Placed.Add(Function("+A1-WE1", main: false, Page3, "c4"));

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(fingerprint, Page1)).Error;

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_backup.Copies);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void APageThatIsGone_IsNotFoundAndNotStarted()
    {
        string fingerprint = Plan(Page1).Fingerprint;
        _pages.Pages.RemoveAll(p => p.Name == Page1);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(fingerprint, Page1)).Error;

        Assert.Equal((ErrorReason.PageNotFound, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    [Fact]
    public void WhenTheBackupFails_NothingIsDeleted()
    {
        string fingerprint = Plan(Page1).Fingerprint;
        _backup.Failure = new IOException("disk full");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(fingerprint, Page1)).Error;

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(_transactions.Calls);
        Assert.Empty(_remover.Removed);
    }

    [Fact]
    public void WhenAPageIsStillThere_EverythingIsRolledBack()
    {
        string fingerprint = Plan(Page1, Page3).Fingerprint;
        _remover.Keep = Page3;

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(fingerprint, Page1, Page3)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack, Page3), (error.Reason, error.Execution, error.Metadata!["notDeleted"]));
        Assert.Contains("Rollback", _transactions.Calls);
        Assert.DoesNotContain("Commit", _transactions.Calls);
    }

    [Fact]
    public void WhenEplanRefuses_EverythingIsRolledBack()
    {
        string fingerprint = Plan(Page1).Fingerprint;
        _remover.Failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not delete a page");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Execute(fingerprint, Page1)).Error;

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
        Assert.Equal(["Begin AI: eplan_page_delete", "CloseUndo", "Rollback", "Dispose"], _transactions.Calls);
    }

    private WritePlan Plan(params string[] pages) =>
        (WritePlan)new PageDeletePlanOperation(new FakeCatalog(_project), _remover).Execute(new PageDeleteArgs { Project = "Line1", Pages = [.. pages] })!;

    private PageDeleteData Execute(string? fingerprint, params string[] pages)
    {
        var operation = new PageDeleteExecuteOperation(new FakeCatalog(_project), _pages, _remover, _backup, _transactions);
        var args = new PageDeleteArgs { Project = "Line1", Pages = [.. pages], Fingerprint = fingerprint, Backup = new BackupTarget { Path = Path.Combine(_folder, "Line1.elk"), MaxSeconds = 31 } };
        return (PageDeleteData)operation.Execute(args)!;
    }

    private static PlacedFunction Function(string name, bool main, string page, string id) => new() { Name = name, IsMain = main, Page = page, Id = id };
}
