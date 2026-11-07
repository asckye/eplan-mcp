using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>cable.reassign.plan and cable.reassign.execute against fakes.</summary>
[Trait("Size", "Small")]
public sealed class CableReassignOperationTests
{
    private readonly FakeCatalog _catalog = new(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    private readonly Cables _cables = new();
    private readonly FakeTransactions _transactions = new();
    private readonly FakePageReader _pages = new();

    [Fact]
    public void Plan_GivesTheScope_AndWritesNothing()
    {
        WritePlan plan = Plan(new CableReassignArgs { Project = "Line1" });
        Assert.Contains("1 connections of 1 cables", Assert.Single(plan.Changes).OldValue, StringComparison.Ordinal);
        Assert.Empty(_cables.Reassigned);
    }

    [Fact]
    public void Plan_AnUnknownCable_IsInvalid()
    {
        var args = new CableReassignArgs { Project = "Line1", Cables = ["+S1-W9"] };
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Execute_BacksUp_ListsWhatChanged_CommitsOnce()
    {
        var args = new CableReassignArgs { Project = "Line1", Cables = ["+S1-W1"], Backup = Backup() };
        args.Fingerprint = Plan(args).Fingerprint;
        CableReassignData data = Execute(args);
        CableCoreChange change = Assert.Single(data.Changed);
        Assert.Equal(("+S1-W1", "-X1:1 -> -M1:U", string.Empty, "1", 1), (change.Cable, change.Connection, change.Previous, change.Core, data.ChangedCount));
        Assert.Equal(["+S1-W1"], _cables.Reassigned.Single());
        Assert.NotNull(data.Backup);
        Assert.Equal(1, _transactions.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void Execute_ChangedSinceThePlan_IsStale()
    {
        var args = new CableReassignArgs { Project = "Line1", Backup = Backup() };
        args.Fingerprint = Plan(args).Fingerprint;
        _cables.Core.Designation = "9";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(args)).Error.Reason);
        Assert.Empty(_cables.Reassigned);
    }

    private static BackupTarget Backup() => new() { Path = Path.Combine(Directory.CreateTempSubdirectory("eplanmcp-cable-").FullName, "Line1.elk"), MaxSeconds = 60 };

    private WritePlan Plan(CableReassignArgs args) => (WritePlan)new CableReassignPlanOperation(_catalog, _cables, _cables).Execute(args)!;

    private CableReassignData Execute(CableReassignArgs args) =>
        (CableReassignData)new CableReassignExecuteOperation(_catalog, _cables, _cables, _cables, _pages, new FakeProjectBackup(_pages), _transactions).Execute(args)!;

    private sealed class Cables : ICableRenumberer, ICableReader, ICableReassigner
    {
        public ConnectionRecord Core { get; } = new() { StartDevice = "-X1", StartPin = "1", EndDevice = "-M1", EndPin = "U" };

        public List<IReadOnlyList<string>?> Reassigned { get; } = [];

        public IReadOnlyList<string> Schemes(string projectPath) => [];

        public IReadOnlyList<PlacedFunction> ReadCables(string projectPath) => [new PlacedFunction { Name = "+S1-W1", IsMain = true, Id = "1" }];

        public void Renumber(string projectPath, IReadOnlyList<string>? cables, string scheme, bool keepExisting, int start, int increment) => throw new NotSupportedException();

        public CableRecord? ReadCable(string projectPath, string tag, string? language) => new() { Category = "Cable", Cores = [Core] };

        public void Reassign(string projectPath, IReadOnlyList<string>? cables, bool overwrite)
        {
            Reassigned.Add(cables);
            Core.Designation = "1";
        }
    }
}
