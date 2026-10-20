using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests;

/// <summary>Assertions on tool results that say why the tool failed, not only that it did.</summary>
internal static class ToolResultAssert
{
    /// <summary>Fails with the result's text, which carries the error envelope, when the tool returned an error.</summary>
    public static void Succeeded(CallToolResult result) =>
        Assert.False(
            result.IsError == true,
            "The tool returned an error: " + string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text)));
}
