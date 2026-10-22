using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// project.create.plan and project.create.execute against a basic project file and a scratch folder in a temporary
/// directory (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.8, 7): only an absent or empty folder is accepted, at the
/// plan and again at the execution; a failed or cancelled creation leaves the folder as it was, or says that it may not.
/// The factory makes the files S27 found (A31): the .elk and the .edb folder.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectCreateOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-create-").FullName;
    private readonly string _file;
    private readonly string _scratch;
    private readonly FakeProjectFactory _factory = new();

    public ProjectCreateOperationTests()
    {
        _file = Path.Combine(_root, "imports", "GB_bas001.zw9");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllBytes(_file, new byte[2048]);
        _scratch = Directory.CreateDirectory(Path.Combine(_root, "scratch", "projects")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Plan_IsOneNewProject_InTheDestination()
    {
        WritePlan plan = Plan(Args("Line1", "a"));

        Assert.Equal(("Line1", Path.Combine(_scratch, "a", "Line1.elk")), (plan.Project, plan.ProjectPath));
        PlannedChange change = Assert.Single(plan.Changes);
        Assert.Equal(("create", "project", "Line1"), (change.Action, change.Kind, change.Name));
        Assert.StartsWith("from basic project GB_bas001.zw9 (2 KB, sha256 ", change.NewValue, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_scratch, "a")));
    }

    [Fact]
    public void Execute_MakesTheProject_InANewFolder_AndReportsWhatWasRead()
    {
        WritePlan plan = Plan(Args("Line1", "a"));

        NewProjectData data = Execute(Args("Line1", "a", plan.Fingerprint));

        Assert.Equal(("Line1", Path.Combine(_scratch, "a", "Line1.elk"), (int?)1, "GB_bas001@1792643412", "18.0"), (data.Name, data.Path, data.Pages, data.ProjectGuid, data.DatabaseVersion));
        Assert.Equal([(Path.Combine(_scratch, "a", "Line1.elk"), _file)], _factory.Calls);
    }

    [Fact]
    public void MissingParents_ArePlannedWithoutCreatingThem_AndMadeOnlyDuringExecute()
    {
        NewProjectArgs args = Args("Line1", "new-parent/project");
        WritePlan plan = Plan(args);
        Assert.False(Directory.Exists(Path.GetDirectoryName(args.Destination)));
        args.Fingerprint = plan.Fingerprint;
        NewProjectData data = Execute(args);
        Assert.Equal("18.0", data.DatabaseVersion);
        Assert.True(File.Exists(data.Path));
    }

    [Fact]
    public void Execute_InAnEmptyFolderThatExists_KeepsTheFolderOnFailure()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "b")).FullName;
        WritePlan plan = Plan(Args("Line1", "b"));
        _factory.Fail = true;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1", "b", plan.Fingerprint)));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public void Execute_AFailureInANewFolder_TakesTheFolderAwayAgain()
    {
        WritePlan plan = Plan(Args("Line1", "c"));
        _factory.Fail = true;

        Assert.Equal(ExecutionState.RolledBack, Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1", "c", plan.Fingerprint))).Error.Execution);
        Assert.False(Directory.Exists(Path.Combine(_scratch, "c")));
    }

    [Fact]
    public void Execute_WithoutTheProjectsFiles_IsEplanOperationFailed_RolledBack()
    {
        WritePlan plan = Plan(Args("Line1", "d"));
        _factory.MakeFiles = false;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1", "d", plan.Fingerprint)));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.False(Directory.Exists(Path.Combine(_scratch, "d")));
    }

    [Fact]
    public void Execute_WhenTheFilesCannotBeDeleted_IsOutcomeUnknown()
    {
        WritePlan plan = Plan(Args("Line1", "e"));
        _factory.Fail = true;
        _factory.HoldOpen = true;
        try
        {
            BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1", "e", plan.Fingerprint)));

            Assert.Equal((ErrorReason.OutcomeUnknown, ExecutionState.Unknown), (error.Error.Reason, error.Error.Execution));
        }
        finally
        {
            _factory.Held?.Dispose();
        }
    }

    [Theory]
    [InlineData("Line1.elk")]
    [InlineData("other.txt")]
    public void PlanAndExecute_AFolderThatIsNotEmpty_IsAlreadyExists_AndNothingIsTouched(string existing)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "f")).FullName;
        WritePlan plan = Plan(Args("Line1", "f"));
        File.WriteAllText(Path.Combine(folder, existing), "keep");

        BridgeErrorException planned = Assert.Throws<BridgeErrorException>(() => Plan(Args("Line1", "f")));
        BridgeErrorException executed = Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1", "f", plan.Fingerprint)));

        Assert.Equal(ErrorReason.AlreadyExists, planned.Error.Reason);
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (executed.Error.Reason, executed.Error.Execution));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, existing)));
        Assert.Empty(_factory.Calls);
    }

    [Fact]
    public void Execute_AFileThatChangedAfterThePlan_IsPlanStale_NotStarted()
    {
        WritePlan plan = Plan(Args("Line1", "g"));
        File.WriteAllBytes(_file, new byte[4096]);

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args("Line1", "g", plan.Fingerprint)));

        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.False(Directory.Exists(Path.Combine(_scratch, "g")));
    }

    [Theory]
    [InlineData("Line.1", "a")]
    [InlineData(" Line1", "a")]
    [InlineData(@"x\Line1", "a")]
    [InlineData("Line1", @"..\a")]
    [InlineData("Line1", "")]
    public void Plan_AnInvalidNameOrDestination_IsInvalidArgument(string name, string folder)
    {
        NewProjectArgs args = Args(name, "a");
        args.Destination = folder.Length == 0 ? "relative" : _scratch + @"\" + folder;

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_ADestinationOnAShare_IsInvalidArgument()
    {
        NewProjectArgs args = Args("Line1", "a");
        args.Destination = @"\\server\share\projects\a";

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    [Fact]
    public void Plan_AFileOfAnotherKind_IsInvalidArgument()
    {
        NewProjectArgs args = Args("Line1", "a");
        args.FilePath = Path.ChangeExtension(_file, ".zw1");

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    private NewProjectArgs Args(string name, string folder, string? fingerprint = null) => new()
    {
        File = "GB_bas001.zw9",
        FilePath = _file,
        Sha256 = new string('a', 64),
        Name = name,
        Destination = Path.Combine(_scratch, folder),
        Fingerprint = fingerprint,
    };

    private static WritePlan Plan(NewProjectArgs args)
    {
        var operation = new ProjectCreatePlanOperation();
        return (WritePlan)operation.Execute(operation.ParseArgs(JObject.FromObject(args)))!;
    }

    private NewProjectData Execute(NewProjectArgs args)
    {
        var operation = new ProjectCreateExecuteOperation(_factory, new FakeSnapshotReader());
        return (NewProjectData)operation.Execute(operation.ParseArgs(JObject.FromObject(args)))!;
    }
}
