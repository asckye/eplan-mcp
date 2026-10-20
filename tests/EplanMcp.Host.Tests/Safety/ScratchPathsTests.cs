using System.Diagnostics;
using System.Runtime.InteropServices;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>SEC-05 on a real file system: each path trick of the threat model (T5) in a temporary directory.</summary>
[Trait("Size", "Medium")]
public sealed class ScratchPathsTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("eplanmcp-scratch-").FullName;
    private readonly string _scratch;
    private readonly string _real;
    private readonly List<string> _junctions = [];

    public ScratchPathsTests()
    {
        _scratch = Directory.CreateDirectory(Path.Combine(_temp, "Scratch")).FullName;
        _real = Directory.CreateDirectory(Path.Combine(_temp, "Projects")).FullName;
    }

    public void Dispose()
    {
        // A junction goes first, on its own: deleting the tree through it is refused.
        _junctions.ForEach(link => Directory.Delete(link));
        Directory.Delete(_temp, recursive: true);
    }

    [Fact]
    public void IsInside_AProjectUnderTheRoot_EvenOneNotCreatedYet()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "Line1"));

        Assert.True(Inside(Path.Combine(_scratch, "Line1", "Line1")));
        Assert.True(Inside(Path.Combine(_scratch, "New", "Project")));
    }

    [Fact]
    public void IsInside_IgnoresCaseAndTrailingSeparators()
    {
        Assert.True(ScratchPaths.IsInside(Path.Combine(_scratch.ToUpperInvariant(), "Line1"), [_scratch.ToLowerInvariant() + Path.DirectorySeparatorChar]));
    }

    [Fact]
    public void IsInside_NotTheRootItselfNorASiblingWithTheSamePrefix()
    {
        string sibling = Directory.CreateDirectory(_scratch + "2").FullName;

        Assert.False(Inside(_scratch));
        Assert.False(Inside(Path.Combine(sibling, "Line1")));
    }

    [Fact]
    public void IsInside_DotDotLeavesTheRoot()
    {
        Assert.False(Inside(Path.Combine(_scratch, "..", "Projects", "Line1")));
        Assert.False(Inside(_scratch + "\\Line1\\..\\..\\Projects\\Line1"));
    }

    [Fact]
    public void IsInside_AJunctionInsideTheRootThatPointsOutside_IsOutside()
    {
        string link = Junction(Path.Combine(_scratch, "Escape"), _real);

        Assert.False(Inside(Path.Combine(link, "Line1")));
    }

    [Fact]
    public void IsInside_AJunctionOutsideThatPointsIntoTheRoot_IsInside()
    {
        // The project really lies in scratch; only the way to it is elsewhere.
        string link = Junction(Path.Combine(_real, "ToScratch"), _scratch);

        Assert.True(Inside(Path.Combine(link, "Line1")));
    }

    [Fact]
    public void IsInside_AShortNameOfTheRoot_IsTheRoot()
    {
        string longRoot = Directory.CreateDirectory(Path.Combine(_temp, "ScratchWithALongName")).FullName;
        string shortRoot = ShortName(longRoot);
        Assert.SkipWhen(string.Equals(shortRoot, longRoot, StringComparison.OrdinalIgnoreCase), "8.3 names are disabled on this volume.");

        Assert.True(ScratchPaths.IsInside(Path.Combine(shortRoot, "Line1"), [longRoot]));
        Assert.True(ScratchPaths.IsInside(Path.Combine(longRoot, "Line1"), [shortRoot]));
    }

    [Theory]
    [InlineData(@"\\server\share\Line1")]
    [InlineData(@"\\?\C:\Scratch\Line1")]
    [InlineData("")]
    public void IsInside_NetworkDeviceAndEmptyPaths_AreOutside(string path)
    {
        Assert.False(ScratchPaths.IsInside(path, [_scratch, @"\\server\share"]));
    }

    private bool Inside(string path) => ScratchPaths.IsInside(path, [_scratch]);

    private string Junction(string link, string target)
    {
        // A junction needs no privilege, unlike a symbolic link; .NET has no API to create one.
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
        _junctions.Add(link);
        return link;
    }

    private static string ShortName(string path)
    {
        char[] buffer = new char[32_768];
        int length = GetShortPathName(path, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : path;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathName(string longPath, [Out] char[] shortPath, int bufferLength);
}
