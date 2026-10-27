using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class ReferenceSourceTests
{
    [Theory]
    [InlineData(ResourceKind.Project, "Demo.elk")]
    [InlineData(ResourceKind.ProjectBackup, "Demo.zw1")]
    public void Source_ReferencedProject_ResolvesOnlyConfiguredRoot(ResourceKind kind, string path)
    {
        PackResource resource = Resource(kind, path);
        Assert.Equal(Path.Combine(@"C:\materials", path), ReferenceCopyService.Source(resource, Roots(@"C:\materials")));
    }

    [Theory]
    [InlineData("../Demo.elk", @"C:\materials")]
    [InlineData("Demo.elk", @"\\server\share")]
    [InlineData("Demo.elk", @"C:\materials\..\other")]
    [InlineData("Demo.elk", @"C:\materials:stream")]
    [InlineData("Demo.elk", "relative")]
    [InlineData("Demo.zw9", @"C:\materials")]
    [InlineData("Demo.elk:stream", @"C:\materials")]
    public void Source_UntrustedPath_IsRejectedBeforeDiskAccess(string path, string root) =>
        Assert.Throws<BridgeCallException>(() => ReferenceCopyService.Source(Resource(ResourceKind.Project, path), Roots(root)));

    [Fact]
    public void Source_BundledWrongKindMissingReferenceOrRoot_IsRejected()
    {
        PackResource resource = Resource(ResourceKind.Project, "Demo.elk");
        foreach (PackResource invalid in new[] { resource with { Storage = ResourceStorage.Bundled }, resource with { Kind = ResourceKind.Macro }, resource with { Reference = null } })
        {
            Assert.Throws<BridgeCallException>(() => ReferenceCopyService.Source(invalid, Roots(@"C:\materials")));
        }
        Assert.Throws<BridgeCallException>(() => ReferenceCopyService.Source(resource, new Dictionary<string, string>()));
    }

    internal static PackResource Resource(ResourceKind kind, string path) => new()
    {
        Path = "references/" + path,
        Kind = kind,
        Storage = ResourceStorage.Referenced,
        Sha256 = new string('a', 64),
        Bytes = 1,
        Files = 1,
        Reference = new ResourceReference { Root = "samples", Path = path },
    };
    private static Dictionary<string, string> Roots(string root) => new() { ["samples"] = root };
}
