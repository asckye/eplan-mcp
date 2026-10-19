namespace EplanMcp.Host.Tests;

[Trait("Size", "Small")]
public sealed class SnapshotDefaultsTests
{
    [Theory]
    [InlineData("a\nb\nc", "a\nx\nb\nc", "+     2: x\n")]
    [InlineData("a\nb\nc", "a\nc", "-     2: b\n")]
    [InlineData("a\nb\nc\nd\ne", "a\nB\nc\nd\nE", "-     2: b\n+     2: B\n-     5: e\n+     5: E\n")]
    [InlineData("x\na\nb\n", "a\nb\nc\n", "-     1: x\n+     3: c\n")]
    public void ChangedLines_ListsOnlyTheLinesThatDiffer(string verified, string received, string expected)
    {
        Assert.Equal(expected, SnapshotDefaults.ChangedLines(verified, received));
    }
}
