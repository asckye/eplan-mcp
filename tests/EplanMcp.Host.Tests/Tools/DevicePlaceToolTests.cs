using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_device_place through the real write pipeline against a fake Bridge (docs/tools/eplan_device_place.md, sections
/// 3, 4 and 7). The commit reads back what S20 found CreateDevice gives a contactor (api-notes A26): the coil placed,
/// 4 contacts unplaced, renamed to the tag.
/// </summary>
[Trait("Size", "Medium")]
public sealed class DevicePlaceToolTests : IDisposable
{
    private const string Page = "=GAA+A1&EFS1/60";
    private const string Part = "SIE.3RH2122-1HB40";

    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);

    public DevicePlaceToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "create", Kind = "device", Name = Page, NewValue = "SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)" }]);
            plan.Details = new Dictionary<string, string> { ["part"] = Part, ["variant"] = "1", ["tag"] = "=GAA+A1-KF20", ["pageSizeKnown"] = "true" };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = _ => new DevicePlaceData
        {
            Project = "Line1",
            Page = Page,
            Part = Part,
            Variant = "1",
            Tag = "=GAA+A1-KF20",
            VisibleTag = "-KF20",
            Category = "Coil",
            Functions = 5,
            Unplaced = 4,
            PageSizeKnown = true,
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ShowsThePlannedTag_AndChangesNothing()
    {
        CallToolResult result = await Place(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, Page, Part, "1"), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("page").GetString(), data.GetProperty("part").GetString(), data.GetProperty("variant").GetString()));
        Assert.Equal("=GAA+A1-KF20", data.GetProperty("device").GetProperty("tag").GetString());
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null), (data.GetProperty("device").GetProperty("visibleTag").ValueKind, data.GetProperty("functions").ValueKind, data.GetProperty("undo").ValueKind, data.GetProperty("hint").ValueKind));
        Assert.Equal("Dry run: would place one device. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
        Assert.False(data.TryGetProperty("symbol", out _));
        Assert.False(data.TryGetProperty("changes", out _));
    }

    [Fact]
    public async Task ACommit_ReturnsTheDeviceAsReadBack_AndPointsToTheUnplacedContacts()
    {
        CallToolResult result = await Place();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_device_place", 5, 4), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("functions").GetInt32(), data.GetProperty("unplaced").GetInt32()));
        JsonElement device = data.GetProperty("device");
        Assert.Equal(("=GAA+A1-KF20", "-KF20", "Coil"), (device.GetProperty("tag").GetString(), device.GetProperty("visibleTag").GetString(), device.GetProperty("category").GetString()));
        Assert.Equal("4 functions of the device (such as its contacts) are on no page yet; the engineer places them in EPLAN, for example from the device navigator.", data.GetProperty("hint").GetString());
        Assert.Equal("Placed a device with 5 functions; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (DevicePlaceArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.DevicePlaceExecute).Args!;
        Assert.Equal((Page, Part, "2", 80.0, 160.0, "-KF20", "f1"), (executed.Page, executed.Part, executed.Variant, executed.X, executed.Y, executed.Tag, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
        Assert.False(data.TryGetProperty("symbol", out _));
        Assert.False(data.TryGetProperty("changes", out _));
    }

    [Fact]
    public async Task ANewPageWithoutASize_IsMentioned()
    {
        _harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = _ => new DevicePlaceData { Project = "Line1", Page = Page, Part = Part, Variant = "1", Tag = "=GAA+A1-KF20", Functions = 1 };

        JsonElement data = WriteToolHarness.Structured(await Place());

        Assert.StartsWith("EPLAN cannot tell this page's size", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecoveredCommit_ShowsThePlannedTag_AndPointsToDeviceGet()
    {
        _harness.Bridge.WriteRecovered = true;

        CallToolResult result = await Place();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal("=GAA+A1-KF20", data.GetProperty("device").GetProperty("tag").GetString());
        Assert.Contains("eplan_device_get", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Equal("Placed a device; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Theory]
    [InlineData(ErrorReason.InvalidArgument, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.AlreadyExists, ExecutionState.NotStarted)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    public async Task AnExecutionError_ReachesTheModelWithItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution, Metadata = new() { ["actual"] = "+A2-KF20" } };

        JsonElement error = WriteToolHarness.Error(await Place());

        Assert.Equal((reason, execution, "+A2-KF20"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("actual").GetString()));
    }

    [Theory]
    [InlineData(ErrorReason.PartNotFound)]
    [InlineData(ErrorReason.AlreadyExists)]
    public async Task APlanError_IsNotStarted(string reason)
    {
        _harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = _ => throw new BridgeCallException(new BridgeError { Reason = reason, Message = "m" });

        JsonElement error = WriteToolHarness.Error(await Place());

        Assert.Equal((reason, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Place());

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_WithApproval_AsksWithTheChangeLine()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        harness.Bridge.Handlers[OperationNames.DevicePlacePlan] = _ => harness.Plan([new PlannedChange { Action = "create", Kind = "device", Name = Page, NewValue = "SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)" }]);
        harness.Bridge.Handlers[OperationNames.DevicePlaceExecute] = _ => new DevicePlaceData { Project = "Line1", Page = Page, Tag = "=GAA+A1-KF20", Functions = 5, PageSizeKnown = true };

        JsonElement data = WriteToolHarness.Structured(await DeviceWriteTools.DevicePlaceAsync(harness.Pipeline, "Line1", Page, 80, 160, "-KF20", Part, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.Equal("create device =GAA+A1&EFS1/60 (SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160))", Assert.Single(WritePipeline.ChangeLines(Assert.Single(channel.Questions).Plan)));
    }

    [Fact]
    public async Task WithoutAProject_TheBridgeIsNotAsked()
    {
        JsonElement error = WriteToolHarness.Error(await DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, " ", Page, 80, 160, "-KF20", Part, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'project' is required", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private Task<CallToolResult> Place(bool dryRun = false) =>
        DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, "Line1", Page, 80, 160, "-KF20", Part, "2", dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
