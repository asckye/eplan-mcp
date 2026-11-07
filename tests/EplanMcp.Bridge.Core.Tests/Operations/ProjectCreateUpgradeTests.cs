using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>The older .zw9 failure and the one-copy upgrade retry from the live A118 case.</summary>
[Trait("Size", "Small")]
public sealed class ProjectCreateUpgradeTests : IDisposable
{
    private const string EplanMessage = "无法创建项目。必须更新所使用的项目模板或基本项目。";
    private readonly string _root = Directory.CreateTempSubdirectory("epup-").FullName;
    private readonly FakeProjectFactory _factory = new();

    public void Dispose()
    {
        _factory.UpgradeHeld?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Older2023BasicProject_UpgradesCopyAndRetries_ThenDeletesUpgradeFolder()
    {
        NewProjectArgs args = Args();
        byte[] original = File.ReadAllBytes(args.FilePath!);
        FailFirstCreate();
        _factory.UpgradeChangesCopy = true;
        using var originalHandle = new FileStream(args.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read);

        NewProjectData data = Execute(args);

        Assert.True(data.Upgraded);
        Assert.Equal(2, _factory.Calls.Count);
        Assert.Equal(args.FilePath, _factory.Calls[0].Source);
        Assert.Equal(Path.Combine(args.UpgradeFolder!, "2023.zw9"), _factory.Calls[1].Source);
        Assert.Equal((args.FilePath, args.UpgradeFolder), Assert.Single(_factory.Upgrades));
        Assert.False(Directory.Exists(args.UpgradeFolder));
        Assert.Equal(original, File.ReadAllBytes(args.FilePath!));
        Assert.True(File.Exists(data.Path));
    }

    [Fact]
    public void UnchangedCopy_ReportsOriginalFailureAndMetadata_WithoutRetry()
    {
        NewProjectArgs args = Args();
        byte[] original = File.ReadAllBytes(args.FilePath!);
        BridgeErrorException failure = FailFirstCreate();

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Same(failure, error);
        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Equal(EplanMessage, error.Error.Metadata!["eplanMessage"]);
        Assert.Equal("unchanged", error.Error.Metadata["upgrade"]);
        Assert.Single(_factory.Calls);
        Assert.False(Directory.Exists(args.UpgradeFolder));
        Assert.Equal(original, File.ReadAllBytes(args.FilePath!));
    }

    [Fact]
    public void BackupFailure_DoesNotTryBasicProjectUpgrade()
    {
        NewProjectArgs args = Args(".zw1");
        FailFirstCreate();
        var plan = new ProjectRestorePlanOperation();
        args.Fingerprint = ((WritePlan)plan.Execute(plan.ParseArgs(JObject.FromObject(args)))!).Fingerprint;
        var execute = new ProjectRestoreExecuteOperation(_factory, new FakeSnapshotReader());

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => execute.Execute(execute.ParseArgs(JObject.FromObject(args))));

        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Empty(_factory.Upgrades);
        Assert.Single(_factory.Calls);
    }

    [Fact]
    public void UpgradeFolderLongerThan80Characters_IsSkippedAndReported()
    {
        NewProjectArgs args = Args();
        args.UpgradeFolder = Path.Combine(_root, new string('a', 81));
        FailFirstCreate();

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal("skipped_path_too_long", error.Error.Metadata!["upgrade"]);
        Assert.Contains("80", error.Error.Metadata["upgradeMessage"], StringComparison.Ordinal);
        Assert.Empty(_factory.Upgrades);
        Assert.Single(_factory.Calls);
        Assert.False(Directory.Exists(args.UpgradeFolder));
    }

    [Fact]
    public void SuccessfulFirstCreate_DeletesUnusedUpgradeFolder()
    {
        NewProjectArgs args = Args();

        NewProjectData data = Execute(args);

        Assert.False(data.Upgraded);
        Assert.Empty(_factory.Upgrades);
        Assert.False(Directory.Exists(args.UpgradeFolder));
    }

    [Fact]
    public void FailedRetry_DoesNotUpgradeAgain_AndDeletesUpgradeFolder()
    {
        NewProjectArgs args = Args();
        FailFirstCreate();
        _factory.FailFirstCreateOnly = false;
        _factory.UpgradeChangesCopy = true;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(args));

        Assert.Equal(ExecutionState.RolledBack, error.Error.Execution);
        Assert.Equal(2, _factory.Calls.Count);
        Assert.Single(_factory.Upgrades);
        Assert.False(Directory.Exists(args.UpgradeFolder));
    }

    [Fact]
    public void UpgradeFolderCleanupFailure_WarnsAndKeepsSuccessfulResult()
    {
        NewProjectArgs args = Args();
        FailFirstCreate();
        _factory.UpgradeChangesCopy = true;
        _factory.HoldUpgradeCopyOpen = true;
        var log = new ListLog();
        var context = new OperationContext("upgrade", write: true, progress: null, log);

        var data = (NewProjectData)context.Run(() => Execute(args))!;

        Assert.True(data.Upgraded);
        Assert.Equal(BridgeLogLevel.Warning, Assert.Single(log.Entries).Level);
        Assert.True(File.Exists(data.Path));
    }

    private BridgeErrorException FailFirstCreate()
    {
        var failure = new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not create a project from the basic project.",
            metadata: new Dictionary<string, string> { ["eplanMessage"] = EplanMessage });
        _factory.Failure = failure;
        _factory.FailFirstCreateOnly = true;
        return failure;
    }

    private NewProjectArgs Args(string extension = ".zw9")
    {
        string source = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "imports")).FullName, "2023" + extension);
        File.WriteAllBytes(source, [2, 0, 2, 3]);
        string upgrade = Directory.CreateDirectory(Path.Combine(_root, ".up", "123abc")).FullName;
        return new NewProjectArgs
        {
            File = Path.GetFileName(source),
            FilePath = source,
            Sha256 = new string('a', 64),
            Name = "Line1",
            Destination = Path.Combine(_root, "scratch", "Line1"),
            UpgradeFolder = upgrade,
        };
    }

    private NewProjectData Execute(NewProjectArgs args)
    {
        var plan = new ProjectCreatePlanOperation();
        args.Fingerprint = ((WritePlan)plan.Execute(plan.ParseArgs(JObject.FromObject(args)))!).Fingerprint;
        var execute = new ProjectCreateExecuteOperation(_factory, new FakeSnapshotReader());
        return (NewProjectData)execute.Execute(execute.ParseArgs(JObject.FromObject(args)))!;
    }
}
