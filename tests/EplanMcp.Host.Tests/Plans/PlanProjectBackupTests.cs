using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Bridge;
using EplanMcp.Host.Tests.Safety;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Medium")]
public sealed class PlanProjectBackupTests : IAsyncDisposable
{
    private const string Project = @"D:\Scratch\Line1";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-initial-backup-").FullName);
    private readonly ManualTime _clock = new(PlanTestData.Created);
    private readonly TaskCompletionSource<JsonNode?> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly BridgeClient _bridge;
    private readonly FakeChannel _channel;
    private readonly PlanProjectBackup _service;
    private ProjectBackupArgs? _args;
    private CancellationToken _nativeToken;

    public PlanProjectBackupTests()
    {
        var endpoint = new BridgeEndpoint("EplanMcpTest.backup", 123);
        var discovery = new FakeDiscovery();
        discovery.Endpoints.Add(endpoint);
        _channel = new FakeChannel(endpoint, (_, parameters, token) =>
        {
            Assert.Equal(OperationNames.ProjectBackup, parameters!["operation"]!.GetValue<string>());
            _args = parameters["args"]!.Deserialize<ProjectBackupArgs>(HostJson.Options)!;
            _nativeToken = token;
            return _reply.Task;
        })
        {
            Handshake = (_, _, _) => Task.FromResult(FakeChannel.CompatibleHandshake(123, operations: [OperationNames.ProjectBackup])),
        };
        _bridge = new BridgeClient(discovery, new FakeConnector(_ => _channel), NullLogger<BridgeClient>.Instance);
        _service = new PlanProjectBackup(_bridge, new BackupFolders(_locations, _clock), _clock, NullLogger<PlanProjectBackup>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _bridge.DisposeAsync();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task Backup_Success_UsesNewFolderAnd60SecondBudget()
    {
        Task<PlanBackupAttempt> run = Run();
        Assert.Equal(Project, _args!.Project);
        Assert.Equal(60, _args.Target!.MaxSeconds);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Folder));
        FinishSuccess();

        PlanBackupAttempt result = await run;

        Assert.Null(result.Error);
        Assert.Equal(new PlanBackup(Copy, 2, 1.5m, true), result.Backup);
        Assert.True(File.Exists(Copy));
        Assert.False(Directory.Exists(Folder + BackupFolders.FailedSuffix));
        Assert.Equal([BridgeProtocol.HandshakeMethod, BridgeProtocol.InvokeMethod], _channel.Methods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_75SecondTimeout_DoesNotMoveLiveCopy_AndSettlesOnlyAfterReply(bool failsLate)
    {
        Task<PlanBackupAttempt> run = Run();
        File.WriteAllText(Copy, "partial copy");
        _clock.Advance(TimeSpan.FromSeconds(74));
        Assert.False(run.IsCompleted);
        _clock.Advance(TimeSpan.FromSeconds(1));

        PlanBackupAttempt result = await run;

        Failed(result, ErrorReason.BackupFailed);
        Assert.False(result.Settlement.IsCompleted);
        Assert.Equal("partial copy", File.ReadAllText(Copy));
        Assert.False(Directory.Exists(Folder + BackupFolders.FailedSuffix));
        if (failsLate)
        {
            FinishFailure(ErrorReason.BackupFailed);
        }
        else
        {
            FinishSuccess();
        }
        await result.Settlement.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(result.Backup!.Usable);
        Assert.False(Directory.Exists(Folder));
        Assert.True(File.Exists(Path.Combine(Folder + BackupFolders.FailedSuffix, "Line1.elk")));
        Assert.Single(_channel.Methods, m => m == BridgeProtocol.InvokeMethod);
    }

    [Fact]
    public async Task Backup_CallerCancellation_LeavesLiveCopyUntilNativeReply()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<PlanBackupAttempt> run = _service.RunAsync(Project, cancel.Token);
        File.WriteAllText(Copy, "partial copy");
        cancel.Cancel();

        PlanBackupAttempt result = await run;

        Failed(result, ErrorReason.Cancelled);
        Assert.True(File.Exists(Copy));
        Assert.False(result.Settlement.IsCompleted);
        Assert.False(_nativeToken.IsCancellationRequested);
        FinishSuccess();
        await result.Settlement.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(Directory.Exists(Folder + BackupFolders.FailedSuffix));
    }

    [Fact]
    public async Task Backup_AlreadyCancelled_CreatesNothingAndSendsNothing()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.RunAsync(Project, cancel.Token));

        Assert.False(Directory.Exists(_locations.Backups));
        Assert.Empty(_channel.Methods);
    }

    [Theory]
    [InlineData(ErrorReason.BackupFailed)]
    [InlineData(ErrorReason.ProjectNotOpen)]
    [InlineData(ErrorReason.Cancelled)]
    public async Task Backup_TerminalFailure_MarksFolderFailed(string reason)
    {
        Task<PlanBackupAttempt> run = Run();
        FinishFailure(reason);

        PlanBackupAttempt result = await run;
        await result.Settlement;

        Failed(result, reason);
        Assert.True(Directory.Exists(Folder + BackupFolders.FailedSuffix));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_ConnectionLost_IsNotProofThatNativeCopyEnded(bool afterTimeout)
    {
        Task<PlanBackupAttempt> run = Run();
        File.WriteAllText(Copy, "partial copy");
        if (afterTimeout)
        {
            _clock.Advance(PlanProjectBackup.WaitLimit);
        }
        _reply.SetException(new IOException("pipe lost"));

        PlanBackupAttempt result = await run;
        await result.Settlement;

        Failed(result, ErrorReason.BackupFailed);
        Assert.Equal("partial copy", File.ReadAllText(Copy));
        Assert.False(Directory.Exists(Folder + BackupFolders.FailedSuffix));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_FolderAlreadyExists_IsNotReusedAndNoCallIsSent(bool nonempty)
    {
        string copy = new BackupFolders(_locations, _clock).Create(Project);
        if (nonempty)
        {
            File.WriteAllText(copy, "keep");
        }

        PlanBackupAttempt result = await Run();

        Assert.Null(result.Backup);
        Assert.Equal(ErrorReason.BackupFailed, result.Error!.Reason);
        Assert.Empty(_channel.Methods);
        Assert.True(Directory.Exists(Path.GetDirectoryName(copy)));
        if (nonempty)
        {
            Assert.Equal("keep", File.ReadAllText(copy));
        }
    }

    [Theory]
    [InlineData("path")]
    [InlineData("pages")]
    [InlineData("negative seconds")]
    [InlineData("over budget")]
    [InlineData("no file")]
    public async Task Backup_InvalidSuccess_IsUnusableAndSettled(string problem)
    {
        Task<PlanBackupAttempt> run = Run();
        var data = new ProjectBackupData { Path = Copy, Pages = 2, Seconds = 1.5m };
        if (problem == "path")
        {
            data.Path = Path.Combine(_locations.Root, "other.elk");
        }
        if (problem == "pages")
        {
            data.Pages = -1;
        }
        if (problem == "negative seconds")
        {
            data.Seconds = -1;
        }
        if (problem == "over budget")
        {
            data.Seconds = 60.001m;
        }
        if (problem != "no file")
        {
            File.WriteAllText(Copy, "unverified copy");
        }
        _reply.SetResult(new JsonObject { ["data"] = JsonSerializer.SerializeToNode(data, HostJson.Options) });

        PlanBackupAttempt result = await run;
        await result.Settlement;

        Failed(result, ErrorReason.BackupFailed);
        Assert.True(Directory.Exists(Folder + BackupFolders.FailedSuffix));
    }

    [Fact]
    public async Task Backup_ClientThrowsSynchronously_ReturnsUnusableAttempt()
    {
        var bridge = new Tools.FakeBridgeClient { Failure = new BridgeError { Reason = ErrorReason.BackupFailed, Execution = ExecutionState.NotStarted } };
        var service = new PlanProjectBackup(bridge, new BackupFolders(_locations, _clock), _clock, NullLogger<PlanProjectBackup>.Instance);

        PlanBackupAttempt result = await service.RunAsync(Project, TestContext.Current.CancellationToken);
        await result.Settlement;

        Failed(result, ErrorReason.BackupFailed);
        Assert.True(Directory.Exists(Path.GetDirectoryName(result.Backup!.Path) + BackupFolders.FailedSuffix));
    }

    private string Copy => _args!.Target!.Path;
    private string Folder => Path.GetDirectoryName(Copy)!;
    private Task<PlanBackupAttempt> Run() => _service.RunAsync(Project, TestContext.Current.CancellationToken);

    private void FinishSuccess()
    {
        File.WriteAllText(Copy, "complete copy");
        _reply.SetResult(new JsonObject { ["data"] = JsonSerializer.SerializeToNode(new ProjectBackupData { Path = Copy, Pages = 2, Seconds = 1.5m }, HostJson.Options) });
    }

    private void FinishFailure(string reason) => _reply.SetException(new BridgeRpcException(
        BridgeProtocol.BusinessErrorCode, "backup failed",
        JsonSerializer.SerializeToNode(new BridgeError { Reason = reason, Execution = ExecutionState.NotStarted }, HostJson.Options)));

    private static void Failed(PlanBackupAttempt result, string reason)
    {
        Assert.False(result.Backup!.Usable);
        Assert.Equal((reason, ExecutionState.NotStarted, false), (result.Error!.Reason, result.Error.Execution, result.Error.Retryable));
    }
}
