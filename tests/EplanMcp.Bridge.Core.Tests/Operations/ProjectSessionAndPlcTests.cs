using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>project.open, project.close and plc.readdress against fakes.</summary>
[Trait("Size", "Small")]
public sealed class ProjectSessionAndPlcTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("eplanmcp-session-").FullName;
    private readonly List<OpenProject> _open = [];
    private readonly Sessions _sessions;
    private readonly Addressing _addressing = new();
    private readonly FakeTransactions _transactions = new();
    private readonly FakePageReader _pages = new();

    public ProjectSessionAndPlcTests()
    {
        _sessions = new Sessions(_open);
        _open.Add(new OpenProject { Name = "Line1", Path = @"D:\Scratch\Line1", Current = true });
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string LinkFile()
    {
        string path = Path.Combine(_folder, "Line2.elk");
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private LiveCatalog Catalog => new(_open);

    [Fact]
    public void Open_PlansOpensAndReportsTheProject()
    {
        var args = new ProjectOpenArgs { Path = LinkFile() };
        var plan = (WritePlan)new ProjectOpenPlanOperation(Catalog).Execute(args)!;
        Assert.Equal(("open", "Line2"), (Assert.Single(plan.Changes).Action, plan.Project));
        Assert.Equal(Path.Combine(_folder, "Line2"), plan.ProjectPath);
        args.Fingerprint = plan.Fingerprint;
        var data = (ProjectSessionData)new ProjectOpenExecuteOperation(Catalog, _sessions).Execute(args)!;
        Assert.Equal(("Line2", Path.Combine(_folder, "Line2"), true), (data.Project, data.Path, data.Current));
    }

    [Fact]
    public void Open_ThePathAsTheProjectListReportsIt_OpensTheElkBesideIt()
    {
        string link = LinkFile();
        var plan = (WritePlan)new ProjectOpenPlanOperation(Catalog).Execute(new ProjectOpenArgs { Path = Path.ChangeExtension(link, null) })!;
        Assert.Equal(("open", Path.ChangeExtension(link, null)), (Assert.Single(plan.Changes).Action, plan.ProjectPath));
    }

    [Fact]
    public void Open_AlreadyOpen_PlansNothing()
    {
        string link = LinkFile();
        _open.Add(new OpenProject { Name = "Line2", Path = Path.ChangeExtension(link, null) });
        Assert.Empty(((WritePlan)new ProjectOpenPlanOperation(Catalog).Execute(new ProjectOpenArgs { Path = link })!).Changes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Line2.elk")]
    [InlineData(@"\\server\share\Line2.elk")]
    [InlineData(@"C:\Line2.zw9")]
    [InlineData(@"C:\does\not\exist\Line2.elk")]
    public void Open_NotALocalExistingElk_IsInvalid(string path) =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new ProjectOpenPlanOperation(Catalog).Execute(new ProjectOpenArgs { Path = path })).Error.Reason);

    [Fact]
    public void Open_EplanDoesNotList_IsFailed_NotStarted()
    {
        var args = new ProjectOpenArgs { Path = LinkFile() };
        args.Fingerprint = ((WritePlan)new ProjectOpenPlanOperation(Catalog).Execute(args)!).Fingerprint;
        _sessions.Ignore = true;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new ProjectOpenExecuteOperation(Catalog, _sessions).Execute(args));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    [Fact]
    public void Close_ClosesAndChecks()
    {
        var args = new ProjectCloseArgs { Project = "Line1" };
        args.Fingerprint = ((WritePlan)new ProjectClosePlanOperation(Catalog).Execute(args)!).Fingerprint;
        Assert.Equal("Line1", ((ProjectSessionData)new ProjectCloseExecuteOperation(Catalog, _sessions).Execute(args)!).Project);
        Assert.Empty(_open);
    }

    [Fact]
    public void Close_EplanKeepsIt_IsFailed()
    {
        var args = new ProjectCloseArgs { Project = "Line1" };
        args.Fingerprint = ((WritePlan)new ProjectClosePlanOperation(Catalog).Execute(args)!).Fingerprint;
        _sessions.Ignore = true;
        Assert.Equal(ErrorReason.EplanOperationFailed, Assert.Throws<BridgeErrorException>(() => new ProjectCloseExecuteOperation(Catalog, _sessions).Execute(args)).Error.Reason);
    }

    [Fact]
    public void Readdress_BacksUp_ListsWhatChanged_CommitsOnce()
    {
        PlcReaddressArgs args = Readdress();
        args.Fingerprint = ReaddressPlan(args).Fingerprint;
        var data = (PlcReaddressData)ReaddressExecute(args);
        PlcAddressChange change = Assert.Single(data.Changed);
        Assert.Equal(("-K1:1", "I1.0", "I0.0", 1), (change.Tag, change.Previous, change.Address, data.ChangedCount));
        Assert.NotNull(data.Backup);
        Assert.Equal(1, _transactions.Calls.Count(c => c == "Commit"));
    }

    [Fact]
    public void Readdress_AddressesChangedSinceThePlan_IsStale()
    {
        PlcReaddressArgs args = Readdress();
        args.Fingerprint = ReaddressPlan(args).Fingerprint;
        _addressing.Points[0].Address = "I9.9";
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => ReaddressExecute(args)).Error.Reason);
    }

    [Fact]
    public void Readdress_NoPoints_PlansNothing()
    {
        _addressing.Points.Clear();
        Assert.Empty(ReaddressPlan(Readdress()).Changes);
    }

    [Fact]
    public void Readdress_UnknownSorting_IsInvalid()
    {
        PlcReaddressArgs args = Readdress();
        args.Sorting = "random";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => ReaddressPlan(args)).Error.Reason);
    }

    private static PlcReaddressArgs Readdress() => new()
    {
        Project = "Line1",
        DigitalInput = "I0.0",
        Sorting = "connection_point",
        Backup = new BackupTarget { Path = Path.Combine(Directory.CreateTempSubdirectory("eplanmcp-plc-").FullName, "Line1.elk"), MaxSeconds = 60 },
    };

    private WritePlan ReaddressPlan(PlcReaddressArgs args) => (WritePlan)new PlcReaddressPlanOperation(Catalog, _addressing).Execute(args)!;

    private object ReaddressExecute(PlcReaddressArgs args) =>
        new PlcReaddressExecuteOperation(Catalog, _addressing, _pages, new FakeProjectBackup(_pages), _transactions).Execute(args)!;

    private sealed class LiveCatalog(List<OpenProject> open) : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [.. open];
    }

    private sealed class Sessions(List<OpenProject> open) : IProjectSessions
    {
        public bool Ignore { get; set; }

        public void Open(string linkFile)
        {
            if (!Ignore)
            { open.Add(new OpenProject { Name = Path.GetFileNameWithoutExtension(linkFile), Path = Path.ChangeExtension(linkFile, null), Current = true }); }
        }

        public void Close(string projectPath)
        {
            if (!Ignore)
            { open.RemoveAll(p => p.Path == projectPath); }
        }
    }

    private sealed class Addressing : IPlcAddressing
    {
        public List<PlcAddressEntry> Points { get; } =
        [
            new() { Id = "1", Tag = "-K1:1", Address = "I1.0" },
            new() { Id = "2", Tag = "-K1:2", Address = "I0.1" },
        ];

        public IReadOnlyList<PlcAddressEntry> Read(string projectPath) => [.. Points.Select(p => new PlcAddressEntry { Id = p.Id, Tag = p.Tag, Address = p.Address })];

        public void Readdress(string projectPath, PlcReaddressArgs args) => Points[0].Address = args.DigitalInput!;
    }
}
