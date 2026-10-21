using System.Diagnostics;
using System.Security.Cryptography;
using EplanMcp.Contracts;
using EplanMcp.Host.Importing;

namespace EplanMcp.Host.Tests.Importing;

/// <summary>SEC-31 on a real file system: the files below the import root, and the junctions the import tools never follow.</summary>
[Trait("Size", "Medium")]
public sealed class ImportFilesTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("eplanmcp-imports-").FullName;
    private readonly HostDataLocations _locations;
    private readonly ImportFiles _files;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public ImportFilesTests()
    {
        _locations = new HostDataLocations(Path.Combine(_temp, "Home"));
        _files = new ImportFiles(_locations);
        _outside = Directory.CreateDirectory(Path.Combine(_temp, "Elsewhere")).FullName;
    }

    public void Dispose()
    {
        // A junction goes first, on its own: deleting the tree through it is refused.
        _junctions.Where(Directory.Exists).ToList().ForEach(link => Directory.Delete(link));
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public async Task AFileBelowTheRoot_IsFound_WithItsSizeAndSha256()
    {
        byte[] content = [.. Enumerable.Range(0, 3000).Select(i => (byte)i)];
        string path = Write(_locations.Imports, @"drawings\Rittal\plan.DXF", content);

        ImportFile file = await _files.ResolveAsync("file", @" drawings\Rittal/plan.DXF ", 100 * 1024 * 1024, TestContext.Current.CancellationToken);

        Assert.Equal(("drawings/Rittal/plan.DXF", path, 3000L), (file.Path, file.FullPath, file.Bytes));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), file.Sha256);
        Assert.Equal(File.GetLastWriteTimeUtc(path), file.LastWriteUtc);
    }

    [Fact]
    public async Task AMissingFile_IsFileNotFound_WithoutTheRootsLocalPath()
    {
        Write(_locations.Imports, "other.dxf", [1]);

        ImportFileException error = await Assert.ThrowsAsync<ImportFileException>(() => _files.ResolveAsync("file", "drawings/plan.dxf", 1024, TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.FileNotFound, ExecutionState.NotStarted, "drawings/plan.dxf"), (error.Error.Reason, error.Error.Execution, error.Error.Metadata!["file"]));
        Assert.Contains("'imports' folder in EplanMcp's data folder", error.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_temp, error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AMissingRoot_IsCreated_SoTheEngineerFindsIt()
    {
        await Assert.ThrowsAsync<ImportFileException>(() => _files.ResolveAsync("file", "plan.dxf", 1024, TestContext.Current.CancellationToken));

        Assert.True(Directory.Exists(_locations.Imports));
    }

    [Fact]
    public async Task AFolderWhereTheFileShouldBe_IsFileNotFound()
    {
        Directory.CreateDirectory(Path.Combine(_locations.Imports, "plan.dxf"));

        ImportFileException error = await Assert.ThrowsAsync<ImportFileException>(() => _files.ResolveAsync("file", "plan.dxf", 1024, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.FileNotFound, error.Error.Reason);
    }

    [Fact]
    public async Task AJunctionBelowTheRoot_IsNeverFollowed()
    {
        Write(_outside, "plan.dxf", [1, 2, 3]);
        Directory.CreateDirectory(_locations.Imports);
        Junction(Path.Combine(_locations.Imports, "linked"), _outside);

        ImportFileException error = await Assert.ThrowsAsync<ImportFileException>(() => _files.ResolveAsync("file", "linked/plan.dxf", 1024, TestContext.Current.CancellationToken));

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Contains("symbolic link or junction", error.Error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.DoesNotContain(_temp, error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheRootItself_MayBeAJunction()
    {
        Write(_outside, "plan.dxf", [1, 2, 3]);
        Directory.CreateDirectory(_locations.Root);
        Junction(_locations.Imports, _outside);

        ImportFile file = await _files.ResolveAsync("file", "plan.dxf", 1024, TestContext.Current.CancellationToken);

        Assert.Equal(3, file.Bytes);
    }

    [Fact]
    public async Task AFileOverTheToolsLimit_IsRefused()
    {
        Write(_locations.Imports, "plan.dxf", new byte[2048]);

        ImportFileException error = await Assert.ThrowsAsync<ImportFileException>(() => _files.ResolveAsync("file", "plan.dxf", 1024, TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal("'file' is 2 KB; this tool imports files of at most 1 KB.", error.Error.Metadata!["violations"]);
    }

    [Fact]
    public async Task AFileOpenWithoutSharing_CannotBeRead()
    {
        string path = Write(_locations.Imports, "plan.dxf", [1]);
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        ImportFileException error = await Assert.ThrowsAsync<ImportFileException>(() => _files.ResolveAsync("file", "plan.dxf", 1024, TestContext.Current.CancellationToken));

        Assert.Contains("cannot be read", error.Error.Message, StringComparison.Ordinal);
    }

    private static string Write(string root, string relative, byte[] content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
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
