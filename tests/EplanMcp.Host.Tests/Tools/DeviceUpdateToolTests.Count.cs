using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

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
    public async Task ReplacePartCrossesPlanAndExecutionAndReportsOldReference()
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        {
            ReplacedPart = new ReplacedPart { PartNumber = "OLD", Variant = "A", Count = 3 }
        };
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync("eplan_device_update", new Dictionary<string, object?>
            { ["project"] = "Line1", ["tag"] = "-K1", ["part"] = "NEW", ["replace_part"] = "OLD", ["dry_run"] = false }, cancellationToken: TestContext.Current.CancellationToken));
            Assert.All(_harness.Bridge.Calls.Where(c => c.Operation is OperationNames.DeviceUpdatePlan or OperationNames.DeviceUpdateExecute),
                c => Assert.Equal("OLD", ((DeviceUpdateArgs)c.Args!).ReplacePart));
            JsonElement old = result.GetProperty("replacedPart");
            Assert.Equal(("OLD", "A", 3), (old.GetProperty("partNumber").GetString(), old.GetProperty("variant").GetString(), old.GetProperty("count").GetInt32()));
            Assert.Contains("update_from_parts", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartOnACable_HintSaysTheCoresStillNeedThePartData(bool dryRun)
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "add", Kind = "part", Name = "+A1-W9", Property = "1", NewValue = "LAPP.00100234" }]);
            plan.Details = new Dictionary<string, string>
            { ["tag"] = "+A1-W9", ["previousTag"] = "+A1-W9", ["part"] = "LAPP.00100234", ["variant"] = "1", ["functions"] = "1", ["cableCores"] = "Cores wait for update_from_parts." };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        { Project = "Line1", Tag = "+A1-W9", PreviousTag = "+A1-W9", Part = "LAPP.00100234", Variant = "1", Functions = 1 };

        JsonElement data = WriteToolHarness.Structured(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "+A1-W9", dry_run: dryRun,
            part: "LAPP.00100234", variant: "1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.EndsWith("Cores wait for update_from_parts.", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
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
