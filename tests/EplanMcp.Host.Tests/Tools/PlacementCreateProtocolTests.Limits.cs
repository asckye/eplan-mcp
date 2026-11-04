using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlacementCreateProtocolTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiftyItems_PreflightAndExecutionUse180Seconds_OtherReadsKeepDefault(bool preview)
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["dry_run"] = preview;
            args["snap_to_grid"] = false;
            args["response_format"] = "detailed";
            args["items"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, 50).Select(i =>
                new { kind = "line", points = new[] { new[] { 10d, 10d + i * 1.5 }, new[] { 15d, 10d + i * 1.5 } } }));
            var call = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.NotEqual(true, call.IsError);
            Assert.Equal([TimeSpan.FromSeconds(180)], harness.Bridge.Timeouts);
            Assert.Equal(50, Assert.IsType<PlacementTextCreateArgs>(harness.Bridge.Calls.Single(c => c.Operation == OperationNames.PlacementTextCreatePlan).Args).Items!.Count);
            if (preview)
            { Assert.Empty(harness.Bridge.Writes); Assert.Empty(store.Items); }
            else
            { Assert.Equal(TimeSpan.FromSeconds(180), Assert.Single(harness.Bridge.Writes).Limit); Assert.Equal(50, store.Items.Count); }
            Assert.Contains(harness.Bridge.Calls, c => c.Operation == OperationNames.ProjectGet);
            Assert.Contains(harness.Bridge.Calls, c => c.Operation == OperationNames.PageGet);
            Assert.Contains(harness.Bridge.Calls, c => c.Operation == OperationNames.PageDrawingGet);
        });
    }

    [Fact]
    public async Task FiftyOneItems_FailsBeforePreflightOrExecution()
    {
        await WithClient(async (client, harness, store) =>
        {
            Dictionary<string, object?> args = Args();
            args["items"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, 51).Select(_ =>
                new { kind = "line", points = (double[][])[[10d, 10d], [15d, 10d]] }));
            var call = await client.CallToolAsync(PlacementCreateTools.Tool, args, cancellationToken: Token);
            Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(call).GetProperty("reason").GetString());
            Assert.Empty(harness.Bridge.Calls);
            Assert.Empty(harness.Bridge.Timeouts);
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(store.Items);
        });
    }
}
