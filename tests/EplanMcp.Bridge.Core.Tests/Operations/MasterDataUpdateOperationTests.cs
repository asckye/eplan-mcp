using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>masterdata.update.plan and masterdata.update.execute against fakes.</summary>
[Trait("Size", "Small")]
public sealed class MasterDataUpdateOperationTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly MasterData _masterData = new();
    private readonly FakePageReader _pages = new();

    [Fact]
    public void Plan_GivesTheScope_AndWritesNothing()
    {
        WritePlan plan = Plan(new MasterDataUpdateArgs { Project = "Line1" });
        Assert.Equal("2 stored files", Assert.Single(plan.Changes).OldValue);
        Assert.Equal(0, _masterData.Updates);
    }

    [Fact]
    public void Plan_NoProject_IsInvalid()
    {
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(new MasterDataUpdateArgs())).Error.Reason);
    }

    [Fact]
    public void Execute_BacksUp_Updates_ListsWhatIsNew()
    {
        var args = new MasterDataUpdateArgs { Project = "Line1", Backup = Backup() };
        args.Fingerprint = Plan(args).Fingerprint;
        MasterDataUpdateData data = Execute(args);
        Assert.Equal((1, 3, "F13_001.f13"), (_masterData.Updates, data.Entries, Assert.Single(data.Added)));
        Assert.NotNull(data.Backup);
    }

    [Fact]
    public void Execute_WithoutAFingerprint_DoesNotStart()
    {
        BridgeErrorException failure = Assert.Throws<BridgeErrorException>(() => Execute(new MasterDataUpdateArgs { Project = "Line1", Backup = Backup() }));
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (failure.Error.Reason, failure.Error.Execution));
        Assert.Equal(0, _masterData.Updates);
    }

    [Fact]
    public void Execute_ChangedSinceThePlan_IsStale()
    {
        var args = new MasterDataUpdateArgs { Project = "Line1", Backup = Backup() };
        args.Fingerprint = Plan(args).Fingerprint;
        _masterData.Stored.Add("F99_001.f99");
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Equal(0, _masterData.Updates);
    }

    private static BackupTarget Backup() => new() { Path = Path.Combine(Directory.CreateTempSubdirectory("eplanmcp-masterdata-").FullName, "Line1.elk"), MaxSeconds = 60 };

    private WritePlan Plan(MasterDataUpdateArgs args) => (WritePlan)new MasterDataUpdatePlanOperation(_catalog, _masterData).Execute(args)!;

    private MasterDataUpdateData Execute(MasterDataUpdateArgs args) =>
        (MasterDataUpdateData)new MasterDataUpdateExecuteOperation(_catalog, _masterData, _pages, new FakeProjectBackup(_pages)).Execute(args)!;

    private sealed class MasterData : IProjectMasterData
    {
        public List<string> Stored { get; } = ["F09_001.f09", "F10_003.f10"];

        public int Updates { get; private set; }

        public IReadOnlyList<string> Entries(string projectPath) => Stored;

        public void Update(string projectPath)
        {
            Updates++;
            Stored.Add("F13_001.f13");
        }
    }
}
