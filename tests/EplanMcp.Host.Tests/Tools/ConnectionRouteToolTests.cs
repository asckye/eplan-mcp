using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_connection_route through the write pipeline with a fake Bridge, inside scratch: no approval.</summary>
[Trait("Size", "Medium")]
public sealed class ConnectionRouteToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new();

    public ConnectionRouteToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.ConnectionRoutePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "update", Kind = "3D connections", Name = "A1", Property = "route", NewValue = "120 along the routing network" }]);
            plan.Details = new Dictionary<string, string> { ["connections"] = "120", ["routedBefore"] = "7", ["routing"] = "network" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.ConnectionRouteExecute] = _ => new ConnectionRouteData
        {
            Project = "Line1",
            Space = "A1",
            Connections = 120,
            Routed = 118,
            Items = [.. Enumerable.Range(0, 120).Select(i => new RoutedConnection
            {
                From = "=GAA+A1-FC" + i,
                FromPin = "2",
                To = "=GAA+A1-QA1",
                ToPin = "#" + i,
                Routed = i < 118,
                LengthMm = i < 118 ? 2000 - i : 0,
            })],
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task DryRun_CountsTheConnections_WithoutRouting()
    {
        JsonElement data = WriteToolHarness.Structured(await RouteAsync(dryRun: true));

        Assert.Equal((true, "not_started", 120, 7), (data.GetProperty("dryRun").GetBoolean(), data.GetProperty("execution").GetString(),
            data.GetProperty("connections").GetInt32(), data.GetProperty("routedBefore").GetInt32()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("items").ValueKind);
        Assert.DoesNotContain(_harness.Bridge.Calls, call => call.Operation == OperationNames.ConnectionRouteExecute);
    }

    [Fact]
    public async Task Execute_ListsAtMostAHundred_AndSaysWhenSomeStayUnrouted()
    {
        CallToolResult result = await RouteAsync(dryRun: false);
        JsonElement data = WriteToolHarness.Structured(result);

        Assert.Equal(("committed", 118, ConnectionRouteTools.MaxItems, true), (data.GetProperty("execution").GetString(), data.GetProperty("routed").GetInt32(),
            data.GetProperty("items").GetArrayLength(), data.GetProperty("truncated").GetBoolean()));
        Assert.Equal(("=GAA+A1-FC0", "#0", 2000d), (data.GetProperty("items")[0].GetProperty("from").GetString(),
            data.GetProperty("items")[0].GetProperty("toPin").GetString(), data.GetProperty("items")[0].GetProperty("lengthMm").GetDouble()));
        Assert.Contains("stay unrouted", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Routed 118 of 120 3D connections in A1.", WriteToolHarness.Text(result, 0));
        var executed = (ConnectionRouteArgs)_harness.Bridge.Calls[^1].Args!;
        Assert.Equal(("+A1", "direct", 1), (executed.Space, executed.Routing, executed.Devices.Count));
    }

    [Fact]
    public async Task MissingProjectOrSpace_IsInvalidBeforeTheBridge()
    {
        JsonElement error = WriteToolHarness.Error(await ConnectionRouteTools.RouteAsync(_harness.Pipeline, new Progress<ProgressNotificationValue>(), "", " ",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Calls);
    }

    private Task<CallToolResult> RouteAsync(bool dryRun) =>
        ConnectionRouteTools.RouteAsync(_harness.Pipeline, new Progress<ProgressNotificationValue>(), "Line1", "+A1", "direct", ["=GAA+A1-QA1"], dryRun,
            TestContext.Current.CancellationToken);
}
