using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ProjectBackupValidationTests
{
    [Theory]
    [InlineData(null, ErrorReason.InvalidArgument)]
    [InlineData("", ErrorReason.InvalidArgument)]
    [InlineData("closed", ErrorReason.ProjectNotOpen)]
    public void Backup_ProjectMustBeExplicitAndOpen(string? project, string reason)
    {
        var backup = new FakeProjectBackup();
        var operation = new ProjectBackupOperation(new FakeCatalog(), new FakePageReader(), backup);

        BridgeError error = Assert.Throws<BridgeErrorException>(() => operation.Execute(new ProjectBackupArgs { Project = project! })).Error;

        Assert.Equal((reason, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.Empty(backup.Copies);
    }

    [Fact]
    public void Backup_CancelBeforeStarting_DoesNotCopy()
    {
        var backup = new FakeProjectBackup();
        var operation = new ProjectBackupOperation(new FakeCatalog(), new FakePageReader(), backup);
        var context = new OperationContext("cancel", write: false, null);
        context.Cancel();

        BridgeError error = Assert.Throws<BridgeErrorException>(() => context.Run(() => operation.Execute(new ProjectBackupArgs()))).Error;

        Assert.Equal((ErrorReason.Cancelled, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Empty(backup.Copies);
    }

    [Fact]
    public void Backup_OperationCanceledException_IsNotConvertedToBackupFailed()
    {
        var cancelled = new OperationCanceledException("Cancelled before the native copy.");
        var backup = new FakeProjectBackup();
        var operation = new ProjectBackupOperation(new CancelledCatalog(cancelled), new FakePageReader(), backup);

        Assert.Same(cancelled, Assert.Throws<OperationCanceledException>(() => operation.Execute(new ProjectBackupArgs { Project = "Line1" })));
        Assert.Empty(backup.Copies);
    }

    private sealed class CancelledCatalog(OperationCanceledException cancelled) : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => throw cancelled;
    }
}
