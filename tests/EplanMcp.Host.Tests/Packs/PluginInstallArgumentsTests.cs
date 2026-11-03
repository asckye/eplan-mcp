using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Packs;

[Trait("Size", "Small")]
public sealed class PluginInstallArgumentsTests
{
    [Theory]
    [InlineData("", "iec-base", "1.0.0")]
    [InlineData(" ", "iec-base", "1.0.0")]
    [InlineData("../local", "iec-base", "1.0.0")]
    [InlineData("D:\\library", "iec-base", "1.0.0")]
    [InlineData("local\n", "iec-base", "1.0.0")]
    [InlineData("local", "", "1.0.0")]
    [InlineData("local", "IEC-BASE", "1.0.0")]
    [InlineData("local", "../iec-base", "1.0.0")]
    [InlineData("local", "iec-base", "")]
    [InlineData("local", "iec-base", "latest")]
    [InlineData("local", "iec-base", ">=1.0.0")]
    [InlineData("local", "iec-base", "1.0.0\n")]
    [InlineData("local", "iec-base", "../1.0.0")]
    public async Task InvalidSelection_IsRejectedBeforeReadingLibraries(string library, string plugin, string version)
    {
        var result = await PluginInstallTools.InstallAsync(null!, null!, library, plugin, version,
            cancellationToken: TestContext.Current.CancellationToken);

        AssertReason(result);
    }

    [Fact]
    public async Task OversizedSelection_IsRejectedBeforeReadingLibraries()
    {
        string oversized = new('a', 129);
        foreach ((string library, string plugin, string version) in new[]
        {
            (oversized, "iec-base", "1.0.0"),
            ("local", oversized, "1.0.0"),
            ("local", "iec-base", oversized),
        })
        {
            var result = await PluginInstallTools.InstallAsync(null!, null!, library, plugin, version,
                cancellationToken: TestContext.Current.CancellationToken);
            AssertReason(result);
        }
    }

    private static void AssertReason(CallToolResult result)
    {
        Assert.True(result.IsError);
        using JsonDocument document = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(ErrorReason.InvalidArgument, document.RootElement.GetProperty("error").GetProperty("reason").GetString());
    }
}
