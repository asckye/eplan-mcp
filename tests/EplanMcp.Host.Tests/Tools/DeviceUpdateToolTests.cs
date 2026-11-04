using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>eplan_device_update through the real write pipeline against a fake Bridge (docs/tools/eplan_device_update.md, sections 3, 4, 7).</summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class DeviceUpdateToolTests : IDisposable
{
    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);

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
    public async Task McpOmittedTrueAndFalse_UsePreviewAndExecutionPaths()
    {
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(OutsideScratchWrites.Approve, channel);
        harness.ProjectPath = Path.Combine(harness.Locations.Root, "outside", "Line1");
        _harness.ProjectPath = harness.ProjectPath;
        harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan];
        harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute];
        await PreviewProtocolHarness.RunAsync(harness, async client =>
        {
            Dictionary<string, object?> args = new() { ["project"] = "Line1", ["tag"] = "=F+A2-KF1", ["new_tag"] = "-KF19" };
            JsonElement omitted = WriteToolHarness.Structured(await client.CallToolAsync("eplan_device_update", args, cancellationToken: TestContext.Current.CancellationToken));
            args["dry_run"] = true;
            JsonElement explicitPreview = WriteToolHarness.Structured(await client.CallToolAsync("eplan_device_update", args, cancellationToken: TestContext.Current.CancellationToken));

            Assert.True(JsonElement.DeepEquals(omitted, explicitPreview));
            Assert.Equal("not_started", omitted.GetProperty("execution").GetString());
            Assert.True(omitted.GetProperty("dryRun").GetBoolean());
            Assert.Equal("f1", omitted.GetProperty("fingerprint").GetString());
            Assert.NotEmpty(omitted.GetProperty("changes").EnumerateArray());
            Assert.Contains("dry_run: false", omitted.GetProperty("hint").GetString(), StringComparison.Ordinal);
            Assert.Empty(harness.Bridge.Writes);
            Assert.Empty(channel.Questions);
            Assert.DoesNotContain(harness.Bridge.Calls, call => call.Operation == OperationNames.DeviceUpdateExecute);
            Assert.False(Directory.Exists(harness.Locations.Audit));

            args["dry_run"] = false;
            JsonElement executed = WriteToolHarness.Structured(await client.CallToolAsync("eplan_device_update", args, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("committed", executed.GetProperty("execution").GetString());
            Assert.False(executed.GetProperty("dryRun").GetBoolean());
            Assert.Equal("f1", executed.GetProperty("fingerprint").GetString());
            Assert.Single(harness.Bridge.Writes);
            Assert.Single(channel.Questions);
            Assert.Equal("=F+A2-KF19", executed.GetProperty("device").GetProperty("tag").GetString());
        });
    }

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
        Assert.Equal("f1", data.GetProperty("fingerprint").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlcRenamePassesTheLiveTagsAndReportsEveryFunction(bool preview)
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        { Project = "Line1", PreviousTag = "=K1+A2-A0015", Tag = "=K1+A2-KE1", Functions = 169 };
        JsonElement result = WriteToolHarness.Structured(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline,
            "Line1", "=K1+A2-A0015", "=K1+A2-KE1", preview, cancellationToken: TestContext.Current.CancellationToken));
        var planned = (DeviceUpdateArgs)_harness.Bridge.Calls.First(c => c.Operation == OperationNames.DeviceUpdatePlan).Args!;
        Assert.Equal(("=K1+A2-A0015", "=K1+A2-KE1"), (planned.Tag, planned.NewTag));
        Assert.Equal(preview ? "not_started" : "committed", result.GetProperty("execution").GetString());
        if (!preview)
        { Assert.Equal(169, result.GetProperty("device").GetProperty("functions").GetInt32()); }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartAssignmentFlowsThroughThePipeline(bool dryRun)
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "add", Kind = "part", Name = "=GAA+A1-SF91", Property = "1", NewValue = "SIE.3SB3400-0B" }]);
            plan.Details = new Dictionary<string, string>
            { ["tag"] = "=GAA+A1-SF91", ["previousTag"] = "=GAA+A1-SF91", ["part"] = "SIE.3SB3400-0B", ["variant"] = "1", ["functions"] = "1" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DeviceUpdateExecute] = _ => new DeviceUpdateData
        { Project = "Line1", Tag = "=GAA+A1-SF91", PreviousTag = "=GAA+A1-SF91", Part = "SIE.3SB3400-0B", Variant = "1", Functions = 1 };

        CallToolResult result = await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "=GAA+A1-SF91", dry_run: dryRun,
            part: "SIE.3SB3400-0B", variant: "1", cancellationToken: TestContext.Current.CancellationToken);

        JsonElement data = WriteToolHarness.Structured(result);
        JsonElement device = data.GetProperty("device");
        Assert.Equal(("=GAA+A1-SF91", "=GAA+A1-SF91", "SIE.3SB3400-0B", "1"),
            (device.GetProperty("tag").GetString(), device.GetProperty("previousTag").GetString(), device.GetProperty("part").GetString(), device.GetProperty("variant").GetString()));
        Assert.Equal(dryRun ? "not_started" : "committed", data.GetProperty("execution").GetString());
        if (dryRun)
        { Assert.Empty(_harness.Bridge.Writes); }
        else
        {
            var args = (DeviceUpdateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.DeviceUpdateExecute).Args!;
            Assert.Equal((string.Empty, "SIE.3SB3400-0B", "1", "f1"), (args.NewTag, args.Part, args.Variant, args.Fingerprint));
        }
    }

    [Fact]
    public async Task RenameAndPartArgumentsArePreservedTogether()
    {
        await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "=F+A2-KF1", "-KF19", dry_run: false, part: "SIE.3SB3400-0B", cancellationToken: TestContext.Current.CancellationToken);
        var args = (DeviceUpdateArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.DeviceUpdateExecute).Args!;
        Assert.Equal(("-KF19", "SIE.3SB3400-0B", (string?)null), (args.NewTag, args.Part, args.Variant));
    }

    [Fact]
    public async Task LegacyPositionalProgressAndCancellationStillWork()
    {
        CallToolResult result = await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "=F+A2-KF1", "-KF19",
            false, null, TestContext.Current.CancellationToken);
        Assert.Equal("committed", WriteToolHarness.Structured(result).GetProperty("execution").GetString());
    }

    [Fact]
    public async Task PartPlanningFailureDoesNotWrite()
    {
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ => throw new BridgeCallException(
            new BridgeError { Reason = ErrorReason.PartNotFound, Message = "Use eplan_part_search." });
        JsonElement error = WriteToolHarness.Error(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "=GAA+A1-SF91",
            part: "missing", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ErrorReason.PartNotFound, error.GetProperty("reason").GetString());
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task PartOnlyPreview_PreservesTheMainFunctionsActualFullName()
    {
        const string main = "=GAA+A1-KF91:1";
        _harness.Bridge.Handlers[OperationNames.DeviceUpdatePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new() { Action = "add", Kind = "part", Name = main, Property = "1", NewValue = "SIE.6ES7131-4BD01-0AA0" }]);
            plan.Details = new() { ["tag"] = main, ["previousTag"] = main, ["part"] = "SIE.6ES7131-4BD01-0AA0", ["variant"] = "1", ["functions"] = "1", ["pages"] = "=GAA+A1&EFS1/60" };
            return plan;
        };
        JsonElement data = WriteToolHarness.Structured(await DeviceWriteTools.DeviceUpdateAsync(_harness.Pipeline, "Line1", "=GAA+A1-KF91", dry_run: true,
            part: "SIE.6ES7131-4BD01-0AA0", cancellationToken: TestContext.Current.CancellationToken));
        JsonElement device = data.GetProperty("device");
        Assert.Equal((main, main, 1), (device.GetProperty("tag").GetString(), device.GetProperty("previousTag").GetString(), device.GetProperty("functions").GetInt32()));
        Assert.Equal(["=GAA+A1&EFS1/60"], device.GetProperty("pages").EnumerateArray().Select(v => v.GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }
}
