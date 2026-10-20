using System.Diagnostics;
using System.Text.RegularExpressions;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Tests.Safety;

namespace EplanMcp.Host.Tests.Exports;

/// <summary>SEC-20 on a real file system: the folder of each export, what may be in it, and how it goes again.</summary>
[Trait("Size", "Medium")]
public sealed partial class ExportFoldersTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 10, 21, 8, 30, 0, TimeSpan.Zero);

    private readonly string _temp = Directory.CreateTempSubdirectory("eplanmcp-exports-").FullName;
    private readonly HostDataLocations _locations;
    private readonly ExportFolders _folders;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public ExportFoldersTests()
    {
        _locations = new HostDataLocations(Path.Combine(_temp, "Home"));
        _folders = new ExportFolders(_locations, new ManualTime(_now));
        _outside = Directory.CreateDirectory(Path.Combine(_temp, "Elsewhere")).FullName;
    }

    public void Dispose()
    {
        // A junction goes first, on its own: deleting the tree through it is refused.
        _junctions.Where(Directory.Exists).ToList().ForEach(link => Directory.Delete(link));
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public void Create_MakesANewEmptyFolder_NamedByProjectTimeAndSuffix()
    {
        ExportFolder folder = _folders.Create("Line1");

        Assert.Matches(StampedName(), Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.Path)));
        Assert.Equal(Path.Combine(_locations.Exports, "Line1") + Path.DirectorySeparatorChar, Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder.Path)) + Path.DirectorySeparatorChar);
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), folder.Path, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder.Path));
    }

    [Fact]
    public void Create_TwiceAtTheSameInstant_NeverReusesAFolder()
    {
        string first = _folders.Create("Line1").Path;
        string second = _folders.Create("Line1").Path;

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(_locations.Exports, "Line1")).Length);
    }

    [Theory]
    [InlineData("Line1", "Line1")]
    [InlineData("电气 项目", "电气 项目")]
    [InlineData("a/b\\c:d", "a_b_c_d")]
    [InlineData("..", ExportFolders.FallbackName)]
    [InlineData("Line1. ", "Line1")]
    [InlineData("con", ExportFolders.FallbackName)]
    [InlineData("", ExportFolders.FallbackName)]
    public void FolderName_IsOneSafeFolderName(string project, string expected)
    {
        Assert.Equal(expected, ExportFolders.FolderName(project));
    }

    [Fact]
    public void Create_ThroughAJunctionAtTheProjectsFolder_IsRefused_AndCreatesNothingThere()
    {
        Directory.CreateDirectory(_locations.Exports);
        Junction(Path.Combine(_locations.Exports, "Line1"), _outside);

        Assert.Throws<ExportFolderException>(() => _folders.Create("Line1"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_outside));
    }

    [Fact]
    public void Create_TheExportRootItselfMayBeAJunction()
    {
        Directory.CreateDirectory(_locations.Root);
        Junction(_locations.Exports, _outside);

        ExportFolder folder = _folders.Create("Line1");

        Assert.Single(Directory.GetDirectories(Path.Combine(_outside, "Line1")));
        Assert.Empty(folder.Files());
    }

    [Fact]
    public void Files_ListsEveryFileBelowTheFolder_WithPathsRelativeToIt()
    {
        ExportFolder folder = _folders.Create("Line1");
        Touch(folder.Path, @"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf", 3);
        Touch(folder.Path, @"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", 5);

        Assert.Equal(
            [new ExportFile(@"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_1.dxf", 5), new ExportFile(@"Line1\=GAA\+A1\&EFS1\=GAA+A1&EFS1_2.dxf", 3)],
            folder.Files());
    }

    [Fact]
    public void Files_WithAJunctionInside_CountAsAFailedExport()
    {
        ExportFolder folder = _folders.Create("Line1");
        Touch(_outside, "secret.txt", 1);
        Junction(Path.Combine(folder.Path, "Linked"), _outside);

        Assert.Throws<ExportFolderException>(folder.Files);
    }

    [Fact]
    public void TryDelete_RemovesOnlyThisFolder_AndNeverFollowsAJunctionInside()
    {
        ExportFolder kept = _folders.Create("Line1");
        ExportFolder folder = _folders.Create("Line1");
        Touch(folder.Path, @"sub\a.pdf", 1);
        Touch(_outside, "keep.txt", 1);
        Junction(Path.Combine(folder.Path, "Linked"), _outside);

        Assert.True(folder.TryDelete());

        Assert.False(Directory.Exists(folder.Path));
        Assert.True(Directory.Exists(kept.Path));
        Assert.True(File.Exists(Path.Combine(_outside, "keep.txt")));
    }

    [Fact]
    public void DoctorExports_ReportsTheFoldersAndBytes()
    {
        Assert.Contains("not created yet", DoctorChecks.Exports(_locations).English, StringComparison.Ordinal);

        Touch(_folders.Create("Line1").Path, "Line1.pdf", 2048);
        Touch(_folders.Create("Line2").Path, @"a\b.dxf", 1024);

        DoctorCheck check = DoctorChecks.Exports(_locations);

        Assert.Equal(DoctorStatus.Info, check.Status);
        Assert.Contains("2 exports, 3 KB in total", check.English, StringComparison.Ordinal);
    }

    [GeneratedRegex("^20261021T083000Z-[a-z0-9]{6}$")]
    private static partial Regex StampedName();

    private static void Touch(string root, string relative, int bytes)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private void Junction(string link, string target)
    {
        // A junction needs no privilege, unlike a symbolic link; .NET has no API to create one.
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        _junctions.Add(link);
    }
}
