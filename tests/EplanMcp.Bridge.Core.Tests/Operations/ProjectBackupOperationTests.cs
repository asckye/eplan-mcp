using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class ProjectBackupOperationTests : IDisposable
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-project-backup-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakeProjectBackup _backup = new();

    private string Copy => Path.Combine(_folder, "Line1.elk");
    private ProjectBackupArgs Args => new() { Project = _project.Path, Target = new() { Path = Copy, MaxSeconds = 60 } };
    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Backup_ValidCopy_ReturnsCheckedCountAndDuration(int count)
    {
        _pages.Pages.AddRange(Enumerable.Range(1, count).Select(i => new PageSummary { Name = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        _backup.Pages = count;
        _backup.Delay = TimeSpan.FromSeconds(2.9);
        var dispatcher = new InlineDispatcher();
        ProjectBackupOperation operation = Operation();
        var router = new OperationRouter([operation], dispatcher, null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken result = router.Invoke(OperationNames.ProjectBackup, JObject.FromObject(Args), "backup");

        Assert.Equal((Copy, count, 2.9m), ((string?)result["path"], (int)result["pages"]!, (decimal)result["seconds"]!));
        Assert.Equal([(_project.Path, Copy)], _backup.Copies);
        Assert.Equal([Copy], _backup.Counted);
        Assert.Equal(1, dispatcher.Calls);
        Assert.Contains(OperationNames.ProjectBackup, router.SupportedOperations);
        Assert.IsNotAssignableFrom<IWriteOperation>(operation);
        Assert.Equal(OutcomeStatus.NotStarted, (string?)router.Invoke(OperationNames.OutcomeGet, JObject.Parse("""{"requestId":"backup"}"""), "get")["status"]);
    }

    [Fact]
    public void Backup_PageCountMismatch_IsBackupFailedAndKeepsCopy()
    {
        _backup.Pages = 1;
        Fails(Args);
        Assert.True(File.Exists(Copy));
        Assert.Single(_backup.Counted);
    }

    [Theory]
    [InlineData("nonempty")]
    [InlineData("missing")]
    [InlineData("target missing")]
    public void Backup_WithoutNewEmptyDestination_DoesNotCopy(string problem)
    {
        ProjectBackupArgs args = Args;
        if (problem == "nonempty")
        {
            File.WriteAllText(Copy, "keep");
        }
        else if (problem == "missing")
        {
            args.Target!.Path = Path.Combine(_folder, "missing", "Line1.elk");
        }
        else
        {
            args.Target = null;
        }

        Fails(args);
        Assert.Empty(_backup.Copies);
        if (problem == "nonempty")
        {
            Assert.Equal("keep", File.ReadAllText(Copy));
        }
    }

    [Fact]
    public void Backup_PreviousDestination_IsNeverReused()
    {
        Operation().Execute(Args);
        Fails(Args);
        Assert.Single(_backup.Copies);
    }

    [Theory]
    [InlineData(60, 120, true)]
    [InlineData(60.01, 120, false)]
    [InlineData(2.01, 2, false)]
    public void Backup_BudgetIsCappedAt60Seconds(double seconds, int requested, bool succeeds)
    {
        _backup.Delay = TimeSpan.FromSeconds(seconds);
        ProjectBackupArgs args = Args;
        args.Target!.MaxSeconds = requested;
        if (succeeds)
        {
            Assert.Equal((decimal)seconds, ((ProjectBackupData)Operation().Execute(args)!).Seconds);
        }
        else
        {
            Fails(args);
        }
        Assert.True(File.Exists(Copy));
    }

    [Fact]
    public void Backup_NativeFailure_UsesExistingFailureSemantics()
    {
        _backup.Failure = new IOException("disk full");
        Fails(Args);
    }

    [Fact]
    public void Backup_CancelDuringCopy_StopsAfterNativeCallAndKeepsCopy()
    {
        var context = new OperationContext("cancel", write: false, null);
        var backup = new CancelOnCopy(_backup, context);
        var operation = new ProjectBackupOperation(new FakeCatalog(_project), _pages, backup);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => operation.Execute(Args))).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.True(File.Exists(Copy));
        Assert.Single(_backup.Counted);
    }

    private ProjectBackupOperation Operation() => new(new FakeCatalog(_project), _pages, _backup, _backup.StartClock);

    private void Fails(ProjectBackupArgs args)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Operation().Execute(args)).Error;
        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, false, ProjectBackups.FailedMessage),
            (error.Reason, error.Execution, error.Retryable, error.Message));
    }

    private sealed class CancelOnCopy(IProjectBackup inner, OperationContext context) : IProjectBackup
    {
        public void Copy(string projectPath, string copyPath)
        {
            inner.Copy(projectPath, copyPath);
            context.Cancel();
        }
        public int CountPages(string copyPath) => inner.CountPages(copyPath);
    }
}
