using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>ADR-0053 (WS-28): error messages and metadata are cleaned like result strings before they reach the model.</summary>
[Trait("Size", "Small")]
public sealed class ToolResultsErrorTests
{
    [Fact]
    public void Error_CleansTheMessageAndMetadata()
    {
        var error = new BridgeError
        {
            Reason = ErrorReason.AlreadyExists,
            Message = "'-K1‮exe.txt\U000E0064' is already used\u0007.",
            Metadata = new() { ["existing"] = "=GAA+A1-K1⁦\U000E0065", ["free"] = "=GAA+A1-K2" },
        };

        CallToolResult result = ToolResults.Error(error);

        JsonNode body = JsonNode.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text)!["error"]!;
        Assert.Equal("'-K1exe.txt' is already used.", (string?)body["message"]);
        Assert.Equal(("=GAA+A1-K1", "=GAA+A1-K2"), ((string?)body["metadata"]!["existing"], (string?)body["metadata"]!["free"]));
    }
}
