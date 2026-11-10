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
    [InlineData(@"\\?\C:\Line2.elk")]
    [InlineData(@"C:Line2.elk")]
    [InlineData(@"C:\projects\CON.elk")]
    [InlineData("C:\\Line\u00012.elk")]
    public void Open_NotALocalElkPath_IsInvalidWhenPlanned(string path) =>
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => new ProjectOpenPlanOperation(Catalog).Execute(new ProjectOpenArgs { Path = path })).Error.Reason);

    [Theory]
    [InlineData(@"C:\Line2.zw9")]
    [InlineData(@"C:\does\not\exist\Line2.elk")]
    public void Open_NoSuchFile_IsCheckedOnlyAfterApproval(string path)
    {
        // Planning runs before approval and must not touch a path outside the scratch roots (WS-29).
        var args = new ProjectOpenArgs { Path = path };
        args.Fingerprint = ((WritePlan)new ProjectOpenPlanOperation(Catalog).Execute(args)!).Fingerprint;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new ProjectOpenExecuteOperation(Catalog, _sessions).Execute(args));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    [Fact]
    public void Open_EplanDoesNotList_IsFailed_NotStarted()
    {
        var args = new ProjectOpenArgs { Path = LinkFile() };
        args.Fingerprint = ((WritePlan)new ProjectOpenPlanOperation(Catalog).Execute(args)!).Fingerprint;
        _sessions.Ignore = true;
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new ProjectOpenExecuteOperation(Catalog, _sessions).Execute(args));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
    }

    private ProjectRenameArgs RenameArgs(string newName)
    {
        string link = LinkFile();
        _open.Add(new OpenProject { Name = "Line2", Path = Path.ChangeExtension(link, null) });
        string backup = Directory.CreateDirectory(Path.Combine(_folder, "backup")).FullName; // a new, empty folder, as the Host makes it
        var args = new ProjectRenameArgs { Project = "Line2", NewName = newName, Backup = new BackupTarget { Path = Path.Combine(backup, "Line2.elk"), MaxSeconds = 150 } };
        args.Fingerprint = ((WritePlan)new ProjectRenamePlanOperation(Catalog).Execute(args)!).Fingerprint;
        return args;
    }

    private ProjectRenameExecuteOperation Rename => new(Catalog, _sessions, new FakeProjectBackup(_pages), _pages);

    [Fact]
    public void Rename_PlansOneRename()
    {
        string link = LinkFile();
        _open.Add(new OpenProject { Name = "Line2", Path = Path.ChangeExtension(link, null) });
        var plan = (WritePlan)new ProjectRenamePlanOperation(Catalog).Execute(new ProjectRenameArgs { Project = "Line2", NewName = "Oven 1" })!;
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("rename", "Line2", "Oven 1", Path.Combine(_folder, "Line2")), (change.Action, change.OldValue, change.NewValue, plan.ProjectPath));
    }

    [Fact]
    public void Rename_CopiesClosesOpensTheCopyAndRemovesTheOldProject()
    {
        var data = (ProjectRenameData)Rename.Execute(RenameArgs("Oven 1"))!;
        Assert.Equal(["copy Oven 1", "close Line2", "open Oven 1", "remove Line2"], _sessions.Calls);
        Assert.Equal(("Oven 1", Path.Combine(_folder, "Oven 1"), Path.Combine(_folder, "Line2")), (data.Project, data.Path, data.PreviousPath));
        Assert.False(File.Exists(Path.Combine(_folder, "Line2.elk")));
        Assert.NotNull(data.Backup);
        Assert.Null(data.Warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Oven.1")]
    [InlineData(" Oven")]
    [InlineData("CON")]
    [InlineData("Line2")]
    [InlineData("Taken")]
    public void Rename_BadOrTakenName_IsInvalid(string name)
    {
        File.WriteAllText(Path.Combine(_folder, "Taken.elk"), string.Empty);
        string link = LinkFile();
        _open.Add(new OpenProject { Name = "Line2", Path = Path.ChangeExtension(link, null) });
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => new ProjectRenamePlanOperation(Catalog).Execute(new ProjectRenameArgs { Project = "Line2", NewName = name }));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Fact]
    public void Rename_CopyNotOpened_PutsTheOldProjectBackAndRemovesTheCopy()
    {
        ProjectRenameArgs args = RenameArgs("Oven 1");
        _sessions.FailOpen = Path.Combine(_folder, "Oven 1.elk");
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Rename.Execute(args));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Contains(_open, p => p.Name == "Line2");
        Assert.False(File.Exists(Path.Combine(_folder, "Oven 1.elk")));
        Assert.Equal("remove Oven 1", _sessions.Calls[^1]);
    }

    [Fact]
    public void Rename_OldFilesLeft_IsAWarningNotAFailure()
    {
        _sessions.KeepOnRemove = true;
        var data = (ProjectRenameData)Rename.Execute(RenameArgs("Oven 1"))!;
        Assert.Equal("Oven 1", data.Project);
        Assert.Contains("left files of the old project", data.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_NameTakenAfterThePlan_IsStale()
    {
        ProjectRenameArgs args = RenameArgs("Oven 1");
        File.WriteAllText(Path.Combine(_folder, "Oven 1.elk"), string.Empty);
        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Rename.Execute(args));
        Assert.Equal(ExecutionState.NotStarted, error.Error.Execution);
        Assert.Empty(_sessions.Calls);
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

        public List<string> Calls { get; } = [];

        /// <summary>The link file EPLAN refuses to open.</summary>
        public string? FailOpen { get; set; }

        /// <summary>Stands for RemoveProject leaving the files.</summary>
        public bool KeepOnRemove { get; set; }

        public void Open(string linkFile)
        {
            Calls.Add("open " + Path.GetFileNameWithoutExtension(linkFile));
            if (string.Equals(linkFile, FailOpen, StringComparison.OrdinalIgnoreCase))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN refused."); }
            if (!Ignore)
            { open.Add(new OpenProject { Name = Path.GetFileNameWithoutExtension(linkFile), Path = Path.ChangeExtension(linkFile, null), Current = true }); }
        }

        public void Close(string projectPath)
        {
            Calls.Add("close " + Path.GetFileName(projectPath));
            if (!Ignore)
            { open.RemoveAll(p => p.Path == projectPath); }
        }

        public void Copy(string linkFile, string newLinkFile)
        {
            Calls.Add("copy " + Path.GetFileNameWithoutExtension(newLinkFile));
            File.WriteAllText(newLinkFile, string.Empty);
        }

        public void Remove(string linkFile)
        {
            Calls.Add("remove " + Path.GetFileNameWithoutExtension(linkFile));
            if (!KeepOnRemove)
            { File.Delete(linkFile); }
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
