using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class PagingTests
{
    private static readonly int[] _ten = [.. Enumerable.Range(0, 10)];

    [Theory]
    [InlineData(0, 3, new[] { 0, 1, 2 })]
    [InlineData(8, 3, new[] { 8, 9 })]
    [InlineData(10, 3, new int[0])]
    public void Slice_ReturnsTheRequestedWindow(int offset, int limit, int[] expected)
    {
        Assert.Equal(expected, Paging.Slice(_ten, new Args { Offset = offset, Limit = limit }));
    }

    [Fact]
    public void Slice_EmptyListAtOffsetZero_IsEmptyNotAnError()
    {
        Assert.Empty(Paging.Slice(Array.Empty<int>(), new Args { Offset = 0, Limit = 5 }));
    }

    [Fact]
    public void Slice_OffsetPastTheEnd_SaysTheCursorIsOutOfDate()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Paging.Slice(_ten, new Args { Offset = 11, Limit = 5 })).Error;

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'cursor' is out of date", error.Metadata!["violations"], StringComparison.Ordinal);
    }

    [Fact]
    public void Slice_BadOffsetAndLimit_AreReportedTogether()
    {
        BridgeError error = Assert.Throws<BridgeErrorException>(() => Paging.Slice(_ten, new Args { Offset = -1, Limit = Paging.MaxLimit + 1 })).Error;

        Assert.Contains("'offset'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Contains("'limit'", error.Metadata["violations"], StringComparison.Ordinal);
    }

    private sealed class Args : ListArgs
    {
    }
}
