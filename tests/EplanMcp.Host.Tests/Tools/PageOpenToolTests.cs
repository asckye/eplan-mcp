using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageOpenToolTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", "\t")]
    public async Task MissingTargets_ReportsBothPointersWithoutCallingBridge(string? project, string? page)
    {
        var bridge = new FakeBridgeClient();
        CallToolResult result = await PageOpenTools.OpenAsync(bridge, project!, page!, TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        string error = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("/project", error, StringComparison.Ordinal);
        Assert.Contains("/page", error, StringComparison.Ordinal);
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task ExactPage_PreservesWhitespaceAndCleansOnlyReturnedData()
    {
        var bridge = new FakeBridgeClient();
        bridge.Handlers[OperationNames.PageOpen] = _ => new PageOpenData { Project = "Line1\u0007", Page = "=A/1\u001b", EditorOpened = true };
        CallToolResult result = await PageOpenTools.OpenAsync(bridge, " Line1 ", " =A/1 ", TestContext.Current.CancellationToken);
        var args = Assert.IsType<PageOpenArgs>(bridge.LastArgs);
        Assert.Equal(("Line1", " =A/1 "), (args.Project, args.Page));
        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(("Line1", "=A/1", true), (data.GetProperty("project").GetString(), data.GetProperty("page").GetString(), data.GetProperty("editor_opened").GetBoolean()));
        Assert.Equal("Page editor opened or focused.", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
        Assert.False(data.TryGetProperty("execution", out _));
        Assert.Empty(bridge.Writes);
    }
}
