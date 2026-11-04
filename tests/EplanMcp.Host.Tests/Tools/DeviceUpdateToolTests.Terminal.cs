using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceUpdateToolTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TerminalPartPreservesExactNameAndDefaultPreview(bool? preview)
    {
        const string tag = "+A1-XD5:1", part = "PXC.3211813";
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new() { Action = "add", Kind = "part", Name = tag, Property = "1", NewValue = part }]);
            plan.Details = new() { ["tag"] = tag, ["previousTag"] = tag, ["part"] = part, ["variant"] = "1", ["functions"] = "1" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        { Project = "Line1", Tag = tag, PreviousTag = tag, Part = part, Variant = "1", Functions = 1 };
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            Dictionary<string, object?> args = new() { ["project"] = "Line1", ["tag"] = tag, ["part"] = part };
            if (preview.HasValue)
            { args["dry_run"] = preview.Value; }
            JsonElement result = WriteToolHarness.Structured(await client.CallToolAsync("eplan_device_update", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(preview != false ? ExecutionState.NotStarted : ExecutionState.Committed, result.GetProperty("execution").GetString());
            Assert.Equal(tag, result.GetProperty("device").GetProperty("tag").GetString());
            var request = (DeviceUpdateArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.DeviceUpdatePlan).Args!;
            Assert.Equal((tag, part, ""), (request.Tag, request.Part, request.NewTag));
            Assert.Equal(preview != false ? 0 : 1, _harness.Bridge.Writes.Count);
        });
    }

    [Fact]
    public async Task AmbiguousTerminalPartFailureDoesNotWrite()
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ => throw new BridgeCallException(
            new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "Part assignment requires one exact terminal; found 2." });
        JsonElement error = WriteToolHarness.Error(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "+A1-XD5:1",
            dry_run: false, part: "PXC.3211813", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Writes);
    }
}
