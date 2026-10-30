using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class PageDisplayTests
{
    [Fact]
    public async Task NotRequested_DoesNotCallBridge_AndReturnsTheSameResult()
    {
        var bridge = new FakeBridgeClient();
        PageDisplay display = await PageDisplay.ShowAsync(bridge, false, "Line1", "P", TestContext.Current.CancellationToken);
        var result = ToolResults.Success("Original summary.", new { hint = "Original hint.", text = "'<>" });
        string before = JsonSerializer.Serialize(result);
        Assert.Same(result, display.Apply(result));
        Assert.Equal(before, JsonSerializer.Serialize(result));
        Assert.Empty(bridge.Calls);
    }

    [Fact]
    public async Task OpenFailed_AppendsHint_WithoutReplacingTheOriginalHint()
    {
        var bridge = new FakeBridgeClient { Failure = new BridgeError { Reason = ErrorReason.EplanBusy, Message = "Close the dialog." } };
        PageDisplay display = await PageDisplay.ShowAsync(bridge, true, "Line1", "P", TestContext.Current.CancellationToken);
        var call = display.Apply(ToolResults.Success("Original summary.", new { hint = "Original hint.", execution = ExecutionState.Committed }));
        JsonElement data = WriteToolHarness.Structured(call);
        Assert.False(data.GetProperty("pageShown").GetBoolean());
        Assert.Equal("Original hint. Page was not shown: Close the dialog.", data.GetProperty("hint").GetString());
        Assert.Equal(ExecutionState.Committed, data.GetProperty("execution").GetString());
        Assert.Equal("Original summary.", WriteToolHarness.Text(call, 0));
    }
}
