using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryScratchPathsTests
{
    private const string Project = @"D:\scratch\writes\LargeWrite";
    private const string LinkFile = Project + ".elk";

    [Fact]
    public void ConnectionStartPinOwnerWithoutExtensionChecksThePhysicalLinkAndEveryAncestor()
    {
        var checkedPaths = new List<string>();
        FileAttributes Attributes(string path)
        {
            checkedPaths.Add(path);
            return path == Project ? throw new FileNotFoundException("ProjectFullName is not a file.")
                : path == LinkFile ? FileAttributes.Normal : FileAttributes.Directory;
        }

        Assert.True(ApiTryScratchPaths.IsApproved(Project, Project, LinkFile, Attributes));
        Assert.Equal([LinkFile, @"D:\scratch\writes", @"D:\scratch", @"D:\", Project + ".edb"], checkedPaths);
    }

    [Theory]
    [InlineData(".elk")]
    [InlineData(".ell")]
    [InlineData(".elr")]
    public void UsesTheNativeLinkExtensionWithoutGuessing(string extension)
    {
        var checkedPaths = new List<string>();
        Assert.True(ApiTryScratchPaths.IsApproved(Project, Project, Project + extension, path => { checkedPaths.Add(path); return FileAttributes.Normal; }));
        Assert.Equal(Project + extension, checkedPaths[0]);
        Assert.Equal(Project + ".edb", checkedPaths[^1]);
    }

    [Theory]
    [InlineData(LinkFile)]
    [InlineData(Project + ".edb")]
    [InlineData(@"D:\scratch\writes")]
    [InlineData(@"D:\scratch")]
    [InlineData(@"D:\")]
    public void RejectsAReparsePointInTheLinkDatabaseOrAnyAncestor(string linked)
    {
        Assert.False(ApiTryScratchPaths.IsApproved(Project, Project, LinkFile,
            path => path == linked ? FileAttributes.ReparsePoint : FileAttributes.Normal));
    }

    [Theory]
    [InlineData("missing-link")]
    [InlineData("missing-database")]
    [InlineData("denied")]
    public void InaccessibleProjectFilesRefuseOwnershipWithoutEscaping(string failure)
    {
        Assert.False(ApiTryScratchPaths.IsApproved(Project, Project, LinkFile, path =>
            failure == "denied" ? throw new UnauthorizedAccessException()
            : path == (failure == "missing-link" ? LinkFile : Project + ".edb") ? throw new FileNotFoundException()
            : FileAttributes.Normal));
    }

    [Theory]
    [InlineData("", Project, LinkFile)]
    [InlineData(@"D:\scratch\Other", Project, LinkFile)]
    [InlineData(Project, @"D:\real\Other", LinkFile)]
    [InlineData(Project, Project, @"D:\real\Other.elk")]
    [InlineData(Project, Project, Project + ".txt")]
    [InlineData(Project, Project, @"D:\scratch\..\writes\LargeWrite.elk")]
    [InlineData(Project, Project, @"\\server\share\LargeWrite.elk")]
    public void InvalidOrMismatchedPathsAreRejectedBeforeReadingAttributes(string approved, string owner, string link)
    {
        Assert.False(ApiTryScratchPaths.IsApproved(approved, owner, link, _ => throw new InvalidOperationException("Must not access files.")));
    }
}
