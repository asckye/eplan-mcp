using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceUpdateToolTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData(2, null)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public async Task CountCrossesProtocolAndPreservesDefaultPreview(int? count, bool? dryRun)
    {
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            Dictionary<string, object?> args = new() { ["project"] = "Line1", ["tag"] = "+A1-WE1", ["part"] = "RIT.9340030" };
            if (count.HasValue)
            { args["count"] = count.Value; }
            if (dryRun.HasValue)
            { args["dry_run"] = dryRun.Value; }
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync("eplan_device_update", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(dryRun != false ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
            DeviceUpdateArgs planned = (DeviceUpdateArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.DeviceUpdatePlan).Args!;
            Assert.Equal(count, planned.Count);
            if (dryRun == false)
            {
                DeviceUpdateArgs executed = (DeviceUpdateArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.DeviceUpdateExecute).Args!;
                Assert.Equal(count, executed.Count);
            }
            else
            { Assert.Empty(_harness.Bridge.Writes); }
        });
    }

    [Fact]
    public async Task CountViolationFromBridgeReturnsWithoutWrite()
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ => throw new BridgeCallException(
            new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "'count' requires 'part' and must be a positive integer." });
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            var result = await client.CallToolAsync("eplan_device_update", new Dictionary<string, object?>
            { ["project"] = "Line1", ["tag"] = "+A1-WE1", ["count"] = 0, ["dry_run"] = false }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.IsError);
            Assert.Empty(_harness.Bridge.Writes);
            Assert.Equal(0, ((DeviceUpdateArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.DeviceUpdatePlan).Args!).Count);
        });
    }
}
