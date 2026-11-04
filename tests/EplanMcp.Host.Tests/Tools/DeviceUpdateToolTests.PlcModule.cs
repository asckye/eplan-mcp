using EplanMcp.Contracts;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceUpdateToolTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlcModuleMergePreservesRequestAndReportsOnlyMovedPoints(bool preview)
    {
        const string source = "=GAA+A1-A?", target = "=GAA+A1-A1";
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new() { Action = "update", Kind = "device", Name = source, Property = "tag", OldValue = source, NewValue = target }]);
            plan.Details = new() { ["tag"] = target, ["previousTag"] = source, ["functions"] = "2", ["pages"] = "circuit" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        { Project = "Line1", Tag = target, PreviousTag = source, Functions = 2, Pages = ["circuit"] };
        var result = WriteToolHarness.Structured(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline,
            "Line1", source, target, preview, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
        var device = result.GetProperty("device");
        Assert.Equal(target, device.GetProperty("tag").GetString());
        Assert.Equal(2, device.GetProperty("functions").GetInt32());
        var request = (DeviceUpdateArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.DeviceUpdatePlan).Args!;
        Assert.Equal((source, target), (request.Tag, request.NewTag));
        Assert.Equal(preview ? 0 : 1, _harness.Bridge.Writes.Count);
    }
}
