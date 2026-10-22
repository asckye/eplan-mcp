using System.Security.Cryptography;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>
/// project.restore.plan and project.restore.execute through the Host's BridgeClient (docs/design/2026-10-21-m6-bridge-snapshot.md,
/// 4.9, 7; README.md). No EplanMcp path makes a project backup, so the test needs one from <see cref="ArchiveVariable"/>,
/// for example S12's backup of the sample project, and copies it below the scratch root itself. Running EPLAN's restore
/// action in the dispatcher is what this test verifies first (S27 used the Remote Client).
/// </summary>
[Trait("Size", "Large")]
public sealed class ProjectRestoreTests(LiveEplan eplan)
{
    /// <summary>The project backup (.zw1) to restore; without it the test is skipped.</summary>
    public const string ArchiveVariable = "EPLANMCP_LARGE_ZW1";

    [Fact]
    public async Task ABackupRestoredTwice_GivesTwoClosedProjectsWithOneHash_AndAFolderWithAFileIsNeverRestoredOnto()
    {
        string? archive = Environment.GetEnvironmentVariable(ArchiveVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(archive) || !File.Exists(archive), $"Set {ArchiveVariable} to a project backup (.zw1) of a sample project, e.g. one made with 'backup /TYPE:PROJECT' on a scratch copy as S12 did (README.md).");
        string folder = OfflineHost.NewFolder(LiveEplan.ScratchRoot(), "restored");
        string file = Path.Combine(folder, "imports", "LargeRestore.zw1");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.Copy(archive!, file);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
        NewProjectArgs Args(string destination) => new() { File = "LargeRestore.zw1", FilePath = file, Sha256 = sha256, Name = "LargeRestore", Destination = Path.Combine(folder, destination) };
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);

        NewProjectData first = await RestoreAsync(bridge, Args("a"));
        NewProjectData second = await RestoreAsync(bridge, Args("b"));

        TestContext.Current.TestOutputHelper?.WriteLine($"restore: {first.Seconds} s and {second.Seconds} s, database version {first.DatabaseVersion}");
        Assert.Equal(("18.0", "18.0"), (first.DatabaseVersion, second.DatabaseVersion)); // A31
        foreach (NewProjectData restored in new[] { first, second })
        {
            Assert.True(File.Exists(restored.Path) && Directory.Exists(Path.ChangeExtension(restored.Path, ".edb")));
            Assert.False(await IsOpenAsync(restored.Path[..^4]), "The restored project is open: the operation opens nothing.");
        }

        LiveSnapshots.Snapshot a = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = first.Path, OpenReadOnly = true });
        LiveSnapshots.Snapshot b = await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = second.Path, OpenReadOnly = true });
        TestContext.Current.TestOutputHelper?.WriteLine(a.Describe());
        Assert.Equal(a.Hash, b.Hash); // S28 r1: a restore keeps every ObjectIdentifier
        Assert.Equal(a.Chunks[0].ProjectGuid, b.Chunks[0].ProjectGuid); // A34: restores keep PROJ_GUID

        // A31: EPLAN would overwrite. A folder with the restored project, and one where a file arrived after the plan.
        BridgeCallException onto = await Assert.ThrowsAsync<BridgeCallException>(() => bridge.InvokeAsync<NewProjectArgs, WritePlan>(OperationNames.ProjectRestorePlan, Args("a"), TestContext.Current.CancellationToken));
        NewProjectArgs late = Args("c");
        late.Fingerprint = (await bridge.InvokeAsync<NewProjectArgs, WritePlan>(OperationNames.ProjectRestorePlan, late, TestContext.Current.CancellationToken)).Fingerprint;
        string sentinel = Path.Combine(Directory.CreateDirectory(late.Destination!).FullName, "LargeRestore.elk");
        File.WriteAllText(sentinel, "engineer's work");
        BridgeCallException refused = await Assert.ThrowsAsync<BridgeCallException>(() => bridge.InvokeWriteAsync<NewProjectArgs, NewProjectData>(
            OperationNames.ProjectRestoreExecute, late, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(120), null, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.AlreadyExists, onto.Error.Reason);
        Assert.Equal((ErrorReason.AlreadyExists, ExecutionState.NotStarted), (refused.Error.Reason, refused.Error.Execution));
        Assert.Equal("engineer's work", File.ReadAllText(sentinel));
        Assert.Equal([sentinel], Directory.EnumerateFileSystemEntries(late.Destination!));
        ProjectCreateTests.Delete(folder);
    }

    private static async Task<NewProjectData> RestoreAsync(BridgeClient bridge, NewProjectArgs args)
    {
        WritePlan plan = await bridge.InvokeAsync<NewProjectArgs, WritePlan>(OperationNames.ProjectRestorePlan, args, TestContext.Current.CancellationToken);
        args.Fingerprint = plan.Fingerprint;
        WriteResult<NewProjectData> result = await bridge.InvokeWriteAsync<NewProjectArgs, NewProjectData>(
            OperationNames.ProjectRestoreExecute, args, Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(180), null, TestContext.Current.CancellationToken);
        return result.Data!;
    }

    private async Task<bool> IsOpenAsync(string project) =>
        (await eplan.CallAsync("eplan_project_list")).GetProperty("projects").EnumerateArray()
            .Any(p => string.Equals(p.GetProperty("path").GetString(), project, StringComparison.OrdinalIgnoreCase));
}
