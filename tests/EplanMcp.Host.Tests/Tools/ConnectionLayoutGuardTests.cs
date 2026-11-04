using EplanMcp.Bridge.Core;
using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Tests.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class ConnectionLayoutGuardTests
{
    [Theory]
    [InlineData("refuse", false)]
    [InlineData("warn", true)]
    public async Task ConnectionPreview_PropagatesGuardAndDoesNotWrite(string guard, bool accepted)
    {
        using var harness = new WriteToolHarness();
        var fixture = new ConnectionCreateFixture();
        fixture.Scene.Page.Placements.Add(new() { Kind = "Function", Location = [16, 8], Bounds = [14, 6, 18, 10], Locator = new() { Type = 17, Oid = 3 } });
        harness.Bridge.Handlers[OperationNames.ConnectionCreatePlan] = args =>
        {
            try
            {
                string request = JsonSerializer.Serialize(args);
                var plan = ConnectionCreatePlanner.Plan(request, fixture.Scene, fixture);
                var result = harness.Plan([new() { Action = "create", Kind = "connection" }]);
                result.Details = new() { ["preview"] = plan.ToString() };
                return result;
            }
            catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
        };
        JsonElement[] items = JsonSerializer.Deserialize<JsonElement[]>(fixture.Request["items"]!.ToString())!;
        var call = await ConnectionCreateTools.CreateAsync(harness.Pipeline, harness.Bridge, "Line1", ConnectionCreateFixture.PageName,
            items, dry_run: true, layout_guard: guard, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(!accepted, call.IsError == true);
        if (accepted)
        { Assert.Contains("R4", WriteToolHarness.Structured(call).GetProperty("warning").GetString(), StringComparison.Ordinal); }
        Assert.Empty(harness.Bridge.Writes);
    }
}
