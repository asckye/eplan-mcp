using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ProjectUpgradeFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("up-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ShortestScratchRoot_GetsAnEmptyFolderWithSixLowercaseHexCharacters()
    {
        string shortest = Path.Combine(_root, "a");
        var config = new HostConfig([Path.Combine(_root, "longer-root"), shortest + Path.DirectorySeparatorChar], OutsideScratchWrites.Deny);

        string folder = Assert.IsType<string>(ProjectCreateTools.PrepareUpgradeFolder(config));

        Assert.Equal(Path.Combine(shortest, ".up"), Path.GetDirectoryName(folder));
        Assert.Matches("^[a-f0-9]{6}$", Path.GetFileName(folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.False(Directory.Exists(config.ScratchRoots[0]));
    }

    [Fact]
    public void LongFolder_IsReturnedForBridgeDiagnosticsWithoutCreatingIt()
    {
        string root = Path.Combine(_root, new string('a', 81));
        var config = new HostConfig([root], OutsideScratchWrites.Deny);

        string folder = Assert.IsType<string>(ProjectCreateTools.PrepareUpgradeFolder(config));

        Assert.True(folder.Length > 80);
        Assert.StartsWith(Path.Combine(root, ".up") + Path.DirectorySeparatorChar, folder, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void NoScratchRoot_HasNoUpgradeFolder()
    {
        Assert.Null(ProjectCreateTools.PrepareUpgradeFolder(new([], OutsideScratchWrites.Deny)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void UnavailableUpgradeParent_DoesNotBlockInitialCreation()
    {
        File.WriteAllText(Path.Combine(_root, ".up"), "keep");

        Assert.Null(ProjectCreateTools.PrepareUpgradeFolder(new([_root], OutsideScratchWrites.Deny)));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_root, ".up")));
    }
}
