using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class PluginLibraryArgumentsTests
{
    [Theory]
    [InlineData(" ", null, null, 50, null)]
    [InlineData("a\n", null, null, 50, null)]
    [InlineData(null, "unknown", null, 50, null)]
    [InlineData(null, "Standard", null, 50, null)]
    [InlineData(null, null, " ", 50, null)]
    [InlineData(null, null, "IEC\n", 50, null)]
    [InlineData(null, null, null, 0, null)]
    [InlineData(null, null, null, 101, null)]
    [InlineData(null, null, null, 50, "")]
    [InlineData(null, null, null, 50, "invalid")]
    public async Task InvalidArguments_AreRejectedBeforeReadingLibraries(string? library, string? kind, string? standard, int limit, string? cursor)
    {
        Assert.True((await PluginLibraryTools.ListAsync(null!, null!, library, kind, standard, limit, cursor,
            TestContext.Current.CancellationToken)).IsError);
    }

    [Fact]
    public async Task LongFilters_AreRejectedBeforeReadingLibraries()
    {
        Assert.True((await PluginLibraryTools.ListAsync(null!, null!, library: new string('a', 101),
            cancellationToken: TestContext.Current.CancellationToken)).IsError);
        Assert.True((await PluginLibraryTools.ListAsync(null!, null!, standard: new string('a', 129),
            cancellationToken: TestContext.Current.CancellationToken)).IsError);
    }

    [Theory]
    [InlineData("other", "standard", "IEC", 1)]
    [InlineData("local", "parts", "IEC", 1)]
    [InlineData("local", "standard", "GB", 1)]
    [InlineData("local", "standard", "IEC", 2)]
    public async Task ChangedFilterOrLimit_RejectsCursorBeforeReadingLibraries(string library, string kind, string standard, int limit)
    {
        string cursor = PageCursor.Encode(1, PageCursor.Fingerprint(PluginLibraryTools.ListTool, "local", "standard", "IEC", "1"));
        Assert.True((await PluginLibraryTools.ListAsync(null!, null!, library, kind, standard, limit, cursor,
            TestContext.Current.CancellationToken)).IsError);
    }
}
