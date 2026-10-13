using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class SessionToolsTests
{
    private readonly FakeBridgeClient _bridge = new();

    [Fact]
    public async Task SessionStatus_Connected_ReportsEplanAndBridge()
    {
        _bridge.Status = new BridgeStatus(
            [new BridgeEndpoint("p", 42)],
            new HandshakeResult
            {
                ProtocolVersion = 1,
                BridgeVersion = "0.1.0",
                Eplan = new EplanInstanceInfo { ProcessId = 42, Version = "2026.0.3.25421", Variant = "Pro Panel", ApiExtensionLicence = true },
            },
            null);

        CallToolResult result = await Status();

        JsonElement data = result.StructuredContent!.Value;
        Assert.True(data.GetProperty("connected").GetBoolean());
        Assert.Equal("Pro Panel", data.GetProperty("eplan").GetProperty("variant").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("hint").ValueKind);
        Assert.Equal("EPLAN 2026.0.3.25421 (Pro Panel) connected, process 42.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task SessionStatus_NotConnected_IsASuccessWithAHint()
    {
        _bridge.Status = new BridgeStatus([], null, BridgeErrors.NotConnected());

        CallToolResult result = await Status();

        JsonElement data = result.StructuredContent!.Value;
        Assert.NotEqual(true, result.IsError);
        Assert.False(data.GetProperty("connected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("eplan").ValueKind);
        Assert.Contains("start EPLAN", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("EPLAN not connected: EPLAN_NOT_CONNECTED", ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionStatus_SeveralInstances_ListsThemAll()
    {
        BridgeEndpoint[] instances = [new("a", 1), new("b", 2)];
        _bridge.Status = new BridgeStatus(instances, null, BridgeErrors.SessionNotSelected(instances));

        JsonElement data = (await Status()).StructuredContent!.Value;

        Assert.Equal([1, 2], data.GetProperty("instances").EnumerateArray().Select(i => i.GetProperty("processId").GetInt32()));
        Assert.Contains("close all but one", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    private Task<CallToolResult> Status() => SessionTools.SessionStatusAsync(_bridge, TestContext.Current.CancellationToken);
}
