using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("refuse", false)]
    [InlineData("warn", true)]
    public async Task LayoutGuard_ProtocolPreservesModeAndDefaultPreview(string? mode, bool allowed)
    {
        await WithClient(async (client, harness, store) =>
        {
            store.Geometry.Placements.Add(new() { Kind = "Function", Location = [20, 20], Bounds = [18, 18, 22, 22], Locator = new() { Type = 17, Oid = 1 } });
            var args = Args();
            args.Remove("dry_run");
            args["items"] = new[] { new { kind = "line", points = (double[][])[[10, 20], [30, 20]] } };
            if (mode is not null) { args["layout_guard"] = mode; }
            var call = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.Equal(!allowed, call.IsError == true);
            if (allowed)
            {
                JsonElement result = WriteToolHarness.Structured(call);
                Assert.Equal(ExecutionState.NotStarted, result.GetProperty("execution").GetString());
                Assert.True(result.GetProperty("dryRun").GetBoolean());
                Assert.Contains("R4", result.GetProperty("warning").GetString(), StringComparison.Ordinal);
                var sent = Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Last(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args);
                Assert.Equal(mode, sent.LayoutGuard);
            }
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(store.Items);
        });
    }
}
