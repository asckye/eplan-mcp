using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ReferencePathTests
{
    [Theory]
    [InlineData(@"C:\source\Demo.elk", @"C:\scratch\new\Copy.elk", @"C:\scratch", true)]
    [InlineData(@"C:\source\Demo.zw1", @"C:\SCRATCH\new\Copy.elk", @"C:\scratch\", true)]
    [InlineData(@"\\server\share\Demo.elk", @"C:\scratch\new\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\Demo.zw9", @"C:\scratch\new\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\..\Demo.elk", @"C:\scratch\new\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\Demo.elk:stream", @"C:\scratch\new\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\Demo.elk", @"C:\scratch-other\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\Demo.elk", @"C:\scratch\..\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\Demo.elk", @"C:\scratch\Copy.zw1", @"C:\scratch", false)]
    [InlineData(@"C:\scratch\Demo.elk", @"C:\scratch\Demo.edb\new\Copy.elk", @"C:\scratch", false)]
    [InlineData(@"C:\source\Demo.elk", @"\\server\share\Copy.elk", @"\\server\share", false)]
    [InlineData("Demo.elk", @"C:\scratch\new\Copy.elk", @"C:\scratch", false)]
    public void ValidPaths_RequiresLocalSourceAndScratchBoundary(string source, string destination, string scratch, bool expected) =>
        Assert.Equal(expected, ReferenceCopies.ValidPaths(new ReferencePrepareArgs { Source = source, Destination = destination, ScratchRoot = scratch }));
}
