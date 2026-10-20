using System.Diagnostics;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The backup folders of Destructive writes (ADR-0013, SEC-21) on a real file system: one new folder per write, named by
/// the UTC time to the millisecond, never reused, never through a link; settled after a write that did not run.
/// </summary>
[Trait("Size", "Medium")]
public sealed class BackupFoldersTests : IDisposable
{
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-backups-").FullName);
    private readonly ManualTime _clock = new(new DateTimeOffset(2026, 10, 21, 8, 30, 0, 123, TimeSpan.Zero));
    private readonly List<string> _junctions = [];

    public void Dispose()
    {
        _junctions.ForEach(link => Directory.Delete(link));
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public void Create_MakesANewEmptyFolderNamedByTheUtcTime_AndNamesTheCopyInIt()
    {
        string copy = Folders().Create(Plan(@"D:\Scratch\Line1"));

        Assert.Equal(Path.Combine(_locations.Backups, "Line1", "20261021T083000123Z", "Line1.elk"), copy);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(copy)!));
        Assert.Equal(Path.Combine(_locations.Backups, "Line1"), Folders().ProjectFolder(Plan(@"D:\Scratch\Line1")));
    }

    [Fact]
    public void Create_NeverReusesAFolder()
    {
        BackupFolders folders = Folders();
        folders.Create(Plan(@"D:\Scratch\Line1"));

        Assert.Throws<IOException>(() => folders.Create(Plan(@"D:\Other\Line1")));
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.EndsWith(@"\20261021T083000124Z\Line1.elk", folders.Create(Plan(@"D:\Other\Line1")), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesAProjectFolderThatIsAJunction()
    {
        string elsewhere = Directory.CreateDirectory(Path.Combine(_locations.Root, "elsewhere")).FullName;
        Directory.CreateDirectory(_locations.Backups);
        Junction(Path.Combine(_locations.Backups, "Line1"), elsewhere);

        Assert.Throws<IOException>(() => Folders().Create(Plan(@"D:\Scratch\Line1")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Theory]
    [InlineData(@"D:\Scratch\")]
    [InlineData(@"D:\Scratch\..")]
    public void Create_RefusesAProjectPathWithoutAFileName(string projectPath)
    {
        Assert.Throws<IOException>(() => Folders().Create(Plan(projectPath)));
    }

    [Fact]
    public void Settle_RenamesAFailedBackup_RemovesAnEmptyFolder_AndKeepsAnythingElse()
    {
        BackupFolders folders = Folders();
        string failed = folders.Create(Plan(@"D:\Scratch\Line1"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        string empty = folders.Create(Plan(@"D:\Scratch\Line1"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        string partial = folders.Create(Plan(@"D:\Scratch\Line1"));
        File.WriteAllText(partial, "part of a copy");

        Assert.Equal(Path.GetDirectoryName(failed) + "-failed", BackupFolders.Settle(failed, failed: true));
        Assert.Null(BackupFolders.Settle(empty, failed: false));
        Assert.Equal(Path.GetDirectoryName(partial) + "-failed", BackupFolders.Settle(partial, failed: false));

        Assert.Equal(["20261021T083000123Z-failed", "20261021T083002123Z-failed"], Directory.EnumerateDirectories(Path.Combine(_locations.Backups, "Line1")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Usage_CountsTheBackupsButNotTheFailedOnes_AndAddsUpEveryFile()
    {
        BackupFolders folders = Folders();
        string kept = folders.Create(Plan(@"D:\Scratch\Line1"));
        File.WriteAllBytes(kept, new byte[1000]);
        _clock.Advance(TimeSpan.FromSeconds(1));
        string failed = folders.Create(Plan(@"D:\Scratch\Line2"));
        File.WriteAllBytes(failed, new byte[24]);
        BackupFolders.Settle(failed, failed: true);

        Assert.Equal((1, 1024L), BackupFolders.Usage(_locations.Backups));
    }

    [Fact]
    public void Failure_IsNotStartedAndNamesNoPath()
    {
        BridgeError error = BackupFolders.Failure();

        Assert.Equal((ErrorReason.BackupFailed, ExecutionState.NotStarted, false), (error.Reason, error.Execution, error.Retryable));
        Assert.DoesNotContain(@":\", error.Message, StringComparison.Ordinal);
    }

    private BackupFolders Folders() => new(_locations, _clock);

    private static WritePlan Plan(string projectPath) => new() { Project = "Line1", ProjectPath = projectPath };

    private void Junction(string link, string target)
    {
        // A junction needs no privilege, unlike a symbolic link; .NET has no API to create one.
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        _junctions.Add(link);
    }
}
