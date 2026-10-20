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
        ToolResultAssert.Succeeded(result);
        Assert.False(data.GetProperty("connected").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("eplan").ValueKind);
        Assert.Contains("start EPLAN", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.StartsWith("EPLAN not connected: EPLAN_NOT_CONNECTED", ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionStatus_SeveralInstancesNoneSelected_ListsThemAllAndPointsAtSessionSelect()
    {
        BridgeEndpoint[] instances = [new("a", 1), new("b", 2)];
        _bridge.Status = new BridgeStatus(instances, null, BridgeErrors.SessionNotSelected(instances));

        JsonElement data = (await Status()).StructuredContent!.Value;

        JsonElement[] listed = [.. data.GetProperty("instances").EnumerateArray()];
        Assert.Equal([1, 2], listed.Select(i => i.GetProperty("processId").GetInt32()));
        Assert.All(listed, i => Assert.False(i.GetProperty("selected").GetBoolean()));
        Assert.Contains("eplan_session_select", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionStatus_SelectedInstance_IsMarkedAndConnected()
    {
        _bridge.Status = new BridgeStatus([new("a", 1), new("b", 2)], Handshake(2, "Pro Panel"), null, SelectedProcessId: 2);

        JsonElement data = (await Status()).StructuredContent!.Value;

        Assert.True(data.GetProperty("connected").GetBoolean());
        Assert.Equal([false, true], data.GetProperty("instances").EnumerateArray().Select(i => i.GetProperty("selected").GetBoolean()));
        Assert.Equal(2, data.GetProperty("eplan").GetProperty("processId").GetInt32());
    }

    [Fact]
    public async Task SessionSelect_PassesTheProcessAndNamesTheChosenEplan()
    {
        _bridge.Status = new BridgeStatus([new("a", 1), new("b", 2)], Handshake(2, "Pro Panel"), null);

        CallToolResult result = await Select(2);

        JsonElement selected = result.StructuredContent!.Value.GetProperty("selected");
        Assert.Equal([2], _bridge.Selections);
        Assert.Equal((2, "2026.0.3.25421", "Pro Panel"), (selected.GetProperty("processId").GetInt32(), selected.GetProperty("version").GetString(), selected.GetProperty("variant").GetString()));
        Assert.Equal("Selected EPLAN 2026.0.3.25421 (Pro Panel), process 2.", ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(result.StructuredContent!.Value.GetRawText(), ((TextContentBlock)result.Content[1]).Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task SessionSelect_ProcessIdNotPositive_IsInvalidWithoutAskingTheBridge(int processId)
    {
        CallToolResult result = await Select(processId);

        Assert.True(result.IsError);
        Assert.Contains(ErrorReason.InvalidArgument, ((TextContentBlock)result.Content[0]).Text, StringComparison.Ordinal);
        Assert.Empty(_bridge.Selections);
    }

    [Fact]
    public async Task SessionSelect_UnknownProcess_IsAToolErrorListingTheCandidates()
    {
        BridgeEndpoint[] instances = [new("a", 1), new("b", 2)];
        _bridge.Failure = BridgeErrors.UnknownInstance(9, instances);

        CallToolResult result = await Select(9);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        JsonElement error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal("1,2", error.GetProperty("metadata").GetProperty("processIds").GetString());
    }

    [Theory]
    [InlineData("2025.0.3.1", "is in the supported range (2024-2027) but EplanMcp has not been tested with it yet")]
    [InlineData("2023.0.3.19351", "is outside the versions EplanMcp supports (2024-2027)")]
    public async Task SessionStatus_AnUntestedEplan_SaysSoInTheHint(string version, string text)
    {
        HandshakeResult handshake = Handshake(42, "Electric P8");
        handshake.Eplan.Version = version;
        _bridge.Status = new BridgeStatus([new BridgeEndpoint("p", 42)], handshake, null);

        CallToolResult result = await Status();

        Assert.Contains(text, result.StructuredContent!.Value.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal($"EPLAN {version} (Electric P8) connected, process 42. See 'hint'.", ((TextContentBlock)result.Content[0]).Text);
    }

    private static HandshakeResult Handshake(int processId, string variant) => new()
    {
        ProtocolVersion = 1,
        BridgeVersion = "0.1.0",
        Eplan = new EplanInstanceInfo { ProcessId = processId, Version = "2026.0.3.25421", Variant = variant },
    };

    private Task<CallToolResult> Status() => SessionTools.SessionStatusAsync(_bridge, new EplanMcp.Host.Safety.SuspensionStore(Path.Combine(Path.GetTempPath(), "eplanmcp-no-state-" + Guid.NewGuid().ToString("N"))), TestContext.Current.CancellationToken);

    private Task<CallToolResult> Select(int processId) => SessionTools.SessionSelectAsync(_bridge, processId, TestContext.Current.CancellationToken);
}
