using System.Diagnostics;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;

namespace EplanMcp.Large.Tests;

/// <summary>
/// Standalone Snapshot backups of open write copies (A18/A41). Sources stay under scratch/writes; destinations are
/// new BackupFolders below the normal data root. Backups are retained, including when a native call is interrupted.
/// </summary>
[Trait("Size", "Large")]
public sealed class ProjectBackupTests(LiveEplan eplan)
{
    [Fact]
    public async Task Backup_OpenScratchProject_OpensReadOnlyWithMatchingPagesAndPreservesProjects()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string project = await WritableAsync(bridge);

        await BackupAndVerifyAsync(bridge, project);
    }

    [Fact]
    public async Task Backup_ReusedFolder_IsRefusedWithoutChangingCopyOrProjects()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string project = await WritableAsync(bridge);
        ProjectBackupData backup = await BackupAndVerifyAsync(bridge, project);
        OpenProject[] before = await OpenAsync(bridge);
        byte[] link = File.ReadAllBytes(backup.Path);
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(Path.ChangeExtension(backup.Path, null));

        BridgeCallException refused = await Assert.ThrowsAsync<BridgeCallException>(() => BackupAsync(bridge, project, backup.Path));

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, false),
            (refused.Error.Reason, refused.Error.Execution, refused.Error.Retryable));
        Assert.Equal(link, File.ReadAllBytes(backup.Path));
        Assert.Equal(files, LiveSnapshots.DesignFiles(Path.ChangeExtension(backup.Path, null)));
        AssertSameProjects(before, await OpenAsync(bridge));
    }

    [Fact]
    public async Task Backup_ZeroPageScratchProject_ProducesReadableEmptyCopy()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        string? empty = null;
        string writes = Path.GetFullPath(WriteHost.ScratchFolder) + Path.DirectorySeparatorChar;
        foreach (OpenProject project in await OpenAsync(bridge))
        {
            if (Path.GetFullPath(project.Path).StartsWith(writes, StringComparison.OrdinalIgnoreCase)
                && (await GetAsync(bridge, project.Path)).Pages == 0)
            {
                empty = project.Path;
                break;
            }
        }

        Assert.SkipWhen(empty is null,
            "No open 0-page scratch copy exists under scratch/writes. Open an empty test copy there to cover this case; the test never creates a project or deletes pages.");

        ProjectBackupData backup = await BackupAndVerifyAsync(bridge, empty!);
        Assert.Equal(0, backup.Pages);
    }

    private static async Task<ProjectBackupData> BackupAndVerifyAsync(BridgeClient bridge, string project)
    {
        ProjectGetData source = await GetAsync(bridge, project);
        OpenProject[] before = await OpenAsync(bridge);
        var folders = new BackupFolders(HostDataLocations.FromEnvironment(), TimeProvider.System);
        string path = folders.Create(project);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!));
        TestContext.Current.TestOutputHelper?.WriteLine("Backup destination (retained after the test): " + path);
        var elapsed = Stopwatch.StartNew();

        ProjectBackupData backup = await BackupAsync(bridge, project, path);
        elapsed.Stop();

        Assert.Equal(path, backup.Path, ignoreCase: true);
        Assert.True(File.Exists(path), "The backup's .elk file is missing.");
        Assert.True(Directory.Exists(Path.ChangeExtension(path, ".edb")), "The backup's data folder is missing.");
        Assert.Equal(source.Pages, backup.Pages);
        Assert.InRange(backup.Seconds, 0.0000001m, 60m);
        Assert.True((double)backup.Seconds <= elapsed.Elapsed.TotalSeconds, "The Bridge duration exceeds the whole call's wall time.");
        OpenProject[] after = await OpenAsync(bridge);
        AssertSameProjects(before, after);
        Assert.DoesNotContain(after, p => string.Equals(p.Path, Path.ChangeExtension(path, null), StringComparison.OrdinalIgnoreCase));

        // The copy is closed above, so OpenReadOnly exercises an actual read-only opening, then closes it (A34).
        LiveSnapshots.Snapshot copy = await LiveSnapshots.TakeAsync(bridge, new() { Project = path, OpenReadOnly = true });

        Assert.Equal(source.Pages, copy.Pages.Count);
        Assert.All(copy.Chunks, chunk =>
        {
            Assert.Equal(source.Pages, chunk.PageCount);
            Assert.Equal(Path.ChangeExtension(path, null), chunk.ProjectPath, ignoreCase: true);
            Assert.Empty(chunk.MissingPages);
        });
        Assert.True(copy.Chunks[^1].Complete);
        Assert.Equal(source.Pages, (await GetAsync(bridge, project)).Pages);
        AssertSameProjects(before, await OpenAsync(bridge));
        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant(
            $"{project}: {backup.Pages} pages, Bridge {backup.Seconds:0.000} s, Host {elapsed.Elapsed.TotalSeconds:0.000} s; read-only copy: {copy.Describe()}"));
        return backup;
    }

    private static Task<ProjectBackupData> BackupAsync(BridgeClient bridge, string project, string path) =>
        bridge.InvokeAsync<ProjectBackupArgs, ProjectBackupData>(OperationNames.ProjectBackup,
            new() { Project = project, Target = new() { Path = path, MaxSeconds = 60 } },
            TimeSpan.FromSeconds(75), null, TestContext.Current.CancellationToken);

    private static Task<ProjectGetData> GetAsync(BridgeClient bridge, string project) =>
        bridge.InvokeAsync<ProjectGetArgs, ProjectGetData>(OperationNames.ProjectGet,
            new() { Project = project }, TestContext.Current.CancellationToken);

    private static async Task<OpenProject[]> OpenAsync(BridgeClient bridge) =>
        [.. (await bridge.InvokeAsync<ProjectListArgs, ProjectListData>(OperationNames.ProjectList,
            new(), TestContext.Current.CancellationToken)).Projects];

    private static async Task<string> WritableAsync(BridgeClient bridge) =>
        WriteHost.OpenedBelow([.. (await OpenAsync(bridge)).Select(p => p.Path)], WriteHost.ScratchFolder, WriteHost.WritableName);

    private static void AssertSameProjects(IEnumerable<OpenProject> expected, IEnumerable<OpenProject> actual) =>
        Assert.Equal(
            expected.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(p => (p.Path, p.Name, p.Current, p.ReadOnly)),
            actual.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(p => (p.Path, p.Name, p.Current, p.ReadOnly)));
}
