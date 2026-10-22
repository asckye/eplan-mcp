using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// project.restore.plan and project.restore.execute against a backup file and a scratch folder in a temporary directory
/// (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.9, 7). EPLAN overwrites a project it restores onto without a word
/// (api-notes A31), so the one thing these tests must show is that a folder with anything in it never reaches EPLAN.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectRestoreOperationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-restore-").FullName;
    private readonly string _file;
    private readonly string _scratch;
    private readonly FakeProjectFactory _factory = new();
    private readonly FakeSnapshotReader _files = new();

    public ProjectRestoreOperationTests()
    {
        _file = Path.Combine(_root, "imports", "references", "Line1.zw1");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllBytes(_file, new byte[5000]);
        _scratch = Directory.CreateDirectory(Path.Combine(_root, "scratch", "references")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void PlanAndExecute_RestoreIntoANewFolder_WithTheDatabaseVersion_AndNoPageCount()
    {
        WritePlan plan = Plan(Args("a"));

        NewProjectData data = Execute(Args("a", plan.Fingerprint));

        Assert.Equal(("create", "project", "Line1"), (plan.Changes[0].Action, plan.Changes[0].Kind, plan.Changes[0].Name));
        Assert.StartsWith("restored from Line1.zw1 (5 KB, sha256 ", plan.Changes[0].NewValue, StringComparison.Ordinal);
        Assert.Equal((Path.Combine(_scratch, "a", "Line1.elk"), "18.0", (int?)null, (string?)null), (data.Path, data.DatabaseVersion, data.Pages, data.ProjectGuid));
        Assert.Equal([(Path.Combine(_scratch, "a", "Line1.elk"), _file)], _factory.Calls);
    }

    [Theory]
    [InlineData("Line1.elk")]
    [InlineData("Line1.edb")]
    [InlineData("notes.txt")]
    public void Restore_OntoAFolderWithAnythingInIt_NeverReachesEplan(string existing)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_scratch, "b")).FullName;
        WritePlan plan = Plan(Args("b"));
        string path = Path.Combine(folder, existing);
        if (existing.EndsWith(".edb", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            File.WriteAllText(path, "engineer's work");
        }

        BridgeErrorException planned = Assert.Throws<BridgeErrorException>(() => Plan(Args("b")));
        BridgeErrorException executed = Assert.Throws<BridgeErrorException>(() => Execute(Args("b", plan.Fingerprint)));

        Assert.Equal(ErrorReason.AlreadyExists, planned.Error.Reason);
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (executed.Error.Reason, executed.Error.Execution));
        Assert.Empty(_factory.Calls);
        Assert.Equal([path], Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public void Restore_AFailure_LeavesNothingBehind_AndIsRolledBack()
    {
        WritePlan plan = Plan(Args("c"));
        _factory.Fail = true;

        BridgeErrorException error = Assert.Throws<BridgeErrorException>(() => Execute(Args("c", plan.Fingerprint)));

        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Error.Reason, error.Error.Execution));
        Assert.False(Directory.Exists(Path.Combine(_scratch, "c")));
    }

    [Fact]
    public void Plan_ABasicProjectInsteadOfABackup_IsInvalidArgument()
    {
        NewProjectArgs args = Args("d");
        args.FilePath = Path.ChangeExtension(_file, ".zw9");

        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
    }

    private NewProjectArgs Args(string folder, string? fingerprint = null) => new()
    {
        File = "references/Line1.zw1",
        FilePath = _file,
        Sha256 = new string('b', 64),
        Name = "Line1",
        Destination = Path.Combine(_scratch, folder),
        Fingerprint = fingerprint,
    };

    private static WritePlan Plan(NewProjectArgs args)
    {
        var operation = new ProjectRestorePlanOperation();
        return (WritePlan)operation.Execute(operation.ParseArgs(JObject.FromObject(args)))!;
    }

    private NewProjectData Execute(NewProjectArgs args)
    {
        var operation = new ProjectRestoreExecuteOperation(_factory, _files);
        return (NewProjectData)operation.Execute(operation.ParseArgs(JObject.FromObject(args)))!;
    }
}
