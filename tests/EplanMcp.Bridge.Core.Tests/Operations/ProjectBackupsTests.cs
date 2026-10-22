using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// The backup step of Destructive writes (ADR-0013, SEC-21): the copy goes only into the Host's new, empty folder and
/// must have the project's page count in time, or the write does not run (BACKUP_FAILED, not_started). Real folders.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectBackupsTests : IDisposable
{
    private static readonly OpenProject _project = new() { Name = "Line1", Path = @"D:\Scratch\Line1" };

    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-backup-").FullName;
    private readonly FakePageReader _pages = new();
    private readonly FakeProjectBackup _backup = new();

    public ProjectBackupsTests()
    {
        _pages.Pages.AddRange([new PageSummary { Name = "=A1/1" }, new PageSummary { Name = "=A1/2" }]);
        _backup.Pages = 2;
    }

    private string Copy => Path.Combine(_folder, "Line1.elk");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ACopyWithThePagesOfTheProject_IsTheBackup()
    {
        string copy = Make(Target());

        Assert.Equal(Copy, copy);
        Assert.Equal([(_project.Path, Copy)], _backup.Copies);
        Assert.Equal([Copy], _backup.Counted);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("relative")]
    [InlineData("unc")]
    [InlineData("dotdot")]
    [InlineData("not elk")]
    [InlineData("no time")]
    [InlineData("folder not empty")]
    [InlineData("no folder")]
    public void WithoutANewEmptyFolder_NothingIsCopied(string problem)
    {
        BackupTarget? target = problem switch
        {
            "missing" => null,
            "relative" => Target(@"backups\Line1.elk"),
            "unc" => Target(@"\\server\share\Line1.elk"),
            "dotdot" => Target(Path.Combine(_folder, "..", Path.GetFileName(_folder), "Line1.elk")),
            "not elk" => Target(Path.Combine(_folder, "Line1.zw1")),
            "no time" => new BackupTarget { Path = Copy, MaxSeconds = 0 },
            "no folder" => Target(Path.Combine(_folder, "gone", "Line1.elk")),
            _ => Target(),
        };
        if (problem == "folder not empty")
        {
            File.WriteAllText(Copy, "an earlier copy");
        }

        BridgeError error = Fails(target);

        Assert.Empty(_backup.Copies);
        Assert.Equal("The write came without a new, empty backup folder.", error.Metadata!["problem"]);
    }

    [Fact]
    public void ACopyThatFails_IsNotStarted()
    {
        _backup.Failure = new InvalidOperationException("disk full");

        BridgeError error = Fails(Target());

        Assert.Equal("EPLAN could not copy the project or read the copy.", error.Metadata!["problem"]);
    }

    [Fact]
    public void ACopyWithOtherPages_IsNoBackup()
    {
        _backup.Pages = 1;

        BridgeError error = Fails(Target());

        Assert.Equal("The copy has 1 pages, the project 2.", error.Metadata!["problem"]);
    }

    [Fact]
    public void ABackupThatTakesExactlyTheLimit_IsTheBackup()
    {
        _backup.Delay = TimeSpan.FromSeconds(1);

        Assert.Equal(Copy, Make(new BackupTarget { Path = Copy, MaxSeconds = 1 }));
    }

    [Fact]
    public void ABackupOverHalfTheLimit_IsNoBackup()
    {
        _backup.Delay = TimeSpan.FromSeconds(1.1);

        BridgeError error = Fails(new BackupTarget { Path = Copy, MaxSeconds = 1 });

        Assert.StartsWith("The backup took 1 s, more than the 1 s", error.Metadata!["problem"], StringComparison.Ordinal);
    }

    [Fact]
    public void AnErrorTheAdapterNames_StaysButIsNotStarted()
    {
        _backup.Failure = new BridgeErrorException(ErrorReason.ProjectNotOpen, "gone");

        BridgeError error = Assert.Throws<BridgeErrorException>(() => Make(Target())).Error;

        Assert.Equal((ErrorReason.ProjectNotOpen, ExecutionState.NotStarted), (error.Reason, error.Execution));
    }

    private BackupTarget Target(string? path = null) => new() { Path = path ?? Copy, MaxSeconds = 90 };

    /// <summary>The backup, timed by the fake EPLAN's own stopwatch: a copy takes exactly its <see cref="FakeProjectBackup.Delay"/>.</summary>
    private string Make(BackupTarget? target) => ProjectBackups.Make(_backup, _pages, _project, target, _backup.StartClock);

    private BridgeError Fails(BackupTarget? target)
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Make(target)).Error;
        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, false, ProjectBackups.FailedMessage), (error.Reason, error.Execution, error.Retryable, error.Message));
        return error;
    }
}
