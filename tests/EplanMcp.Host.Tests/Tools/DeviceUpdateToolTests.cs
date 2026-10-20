using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_device_update through the real write pipeline against a fake Bridge (docs/tools/eplan_device_update.md, sections 3, 4, 7).</summary>
[Trait("Size", "Medium")]
public sealed class DeviceUpdateToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new();

    public DeviceUpdateToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "update", Kind = "device", Name = "=F+A2-KF1", Property = "tag", OldValue = "=F+A2-KF1", NewValue = "=F+A2-KF19" }]);
            plan.Details = new Dictionary<string, string> { ["functions"] = "3", ["pageCount"] = "2", ["pages"] = "=F+A2&EFS1/1\n=K1+A2&EFS1/2" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        {
            Project = "Line1",
            Tag = "=F+A2-KF19",
            PreviousTag = "=F+A2-KF1",
            VisibleTag = "-KF19",
            Functions = 3,
            Pages = ["=F+A2&EFS1/1", "=K1+A2&EFS1/2"],
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ShowsThePlannedTagFunctionsAndPages()
    {
        CallToolResult result = await Update(dryRun: true);

        JsonElement device = WriteToolHarness.Structured(result).GetProperty("device");
        Assert.Equal(("=F+A2-KF19", "=F+A2-KF1", JsonValueKind.Null, 3), (device.GetProperty("tag").GetString(), device.GetProperty("previousTag").GetString(), device.GetProperty("visibleTag").ValueKind, device.GetProperty("functions").GetInt32()));
        Assert.Equal(["=F+A2&EFS1/1", "=K1+A2&EFS1/2"], device.GetProperty("pages").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("Dry run: would rename a device and its 2 auxiliary functions. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ACommit_ReturnsTheDeviceAsReadBack()
    {
        CallToolResult result = await Update();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_device_update", "-KF19"), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("device").GetProperty("visibleTag").GetString()));
        Assert.Equal("Renamed a device and its 2 auxiliary functions; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (DeviceUpdateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.DeviceUpdateExecute).Args!;
        Assert.Equal(("=F+A2-KF1", "-KF19", "f1"), (executed.Tag, executed.NewTag, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task ARecoveredCommit_ShowsThePlanAndPointsToDeviceGet()
    {
        _harness.Bridge.WriteRecovered = true;

        JsonElement data = WriteToolHarness.Structured(await Update());

        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal("=F+A2-KF19", data.GetProperty("device").GetProperty("tag").GetString());
        Assert.Contains("eplan_device_get", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Update());

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task WithoutAProject_TheBridgeIsNotAsked()
    {
        JsonElement error = WriteToolHarness.Error(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, " ", "-KF1", "-KF19", false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'project' is required", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private Task<CallToolResult> Update(bool dryRun = false) =>
        DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "=F+A2-KF1", "-KF19", dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
