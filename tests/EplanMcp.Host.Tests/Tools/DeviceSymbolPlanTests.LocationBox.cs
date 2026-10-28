using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    [Fact]
    public async Task LocationBox_DryRunReportsNoAffectedDevicesAndWritesNothing()
    {
        JsonElement result = WriteToolHarness.Structured(await Box());
        JsonElement box = result.GetProperty("locationBox");
        Assert.Equal(("+P1", "dashed"), (box.GetProperty("structure").GetString(), box.GetProperty("outline").GetString()));
        Assert.Equal([60d, 140d, 100d, 180d], box.GetProperty("bounds").EnumerateArray().Select(n => n.GetDouble()));
        Assert.Empty(box.GetProperty("affectedDevices").EnumerateArray());
        Assert.Contains("devices keep their own structure", Assert.Single(result.GetProperty("changes").EnumerateArray()).GetProperty("newValue").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, result.GetProperty("functions").GetInt32());
        Assert.Empty(_harness.Bridge.Writes);
        Assert.Empty(_placement.Calls);
        Assert.Equal(0, _store.PartReads);
    }

    [Fact]
    public async Task LocationBox_CommitKeepsDeviceStructureAndClosesOneUndo_RepeatConflicts()
    {
        _store.Names.Add("=F+A1-K1");
        JsonElement result = WriteToolHarness.Structured(await Box(dryRun: false));
        Assert.Equal(("committed", "AI: eplan_device_place"), (result.GetProperty("execution").GetString(), result.GetProperty("undo").GetString()));
        Assert.Equal(["=F+A1-K1"], _store.Names);
        Assert.Empty(result.GetProperty("locationBox").GetProperty("affectedDevices").EnumerateArray());
        Assert.Equal(["Begin", "Write", "Readback", "ReleaseLists", "Commit", "CloseUndo", "Dispose"], _placement.Calls);
        Assert.Equal(("SPECIAL", "SC", 0), (_placement.Resolved!.Library, _placement.Resolved.Name, _placement.Resolved.Variant));
        Assert.Equal(ErrorReason.AlreadyExists, WriteToolHarness.Error(await Box(structure: "+p1")).GetProperty("reason").GetString());
        Assert.Single(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("Readback")]
    [InlineData("Structure")]
    [InlineData("Outline")]
    [InlineData("Bounds")]
    [InlineData("Affected")]
    public async Task LocationBox_FailureOrMismatchedReadbackRollsBackWithoutAnEmptyUndo(string stage)
    {
        _store.Names.Add("=F+A1-K1");
        _placement.FailAt = stage;
        JsonElement error = WriteToolHarness.Error(await Box(dryRun: false));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(["=F+A1-K1"], _store.Names);
        Assert.Equal(["page:1"], _store.Page.Objects);
        Assert.Empty(_store.Page.LocationBoxStructures);
        Assert.Equal(["ReleaseLists", "Abort", "Dispose"], _placement.Calls.TakeLast(3));
        Assert.DoesNotContain("Commit", _placement.Calls);
        Assert.DoesNotContain("CloseUndo", _placement.Calls);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("outside")]
    [InlineData("type")]
    [InlineData("nonfinite")]
    public async Task LocationBox_UnknownOrInvalidPageBoundsAndWrongPageTypeAreRejected(string problem)
    {
        switch (problem)
        {
            case "unknown":
                _store.Page.Width = null;
                break;
            case "outside":
                _store.Page.Width = 90;
                break;
            case "type":
                _store.Page.Type = "Overview";
                break;
            case "nonfinite":
                _store.Page.Height = double.NaN;
                break;
        }
        Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(await Box()).GetProperty("reason").GetString());
        Assert.Empty(_placement.Calls);
    }

    [Fact]
    public async Task LocationBox_RequiresTheVerifiedNativeSymbol()
    {
        _store.Symbols.RemoveAt(1);
        Assert.Equal(ErrorReason.SymbolNotFound, WriteToolHarness.Error(await Box()).GetProperty("reason").GetString());
        _store.Symbols.Add(new() { Library = "SPECIAL", Name = "SC", Type = "Function", Kind = "function" });
        Assert.Equal(ErrorReason.CapabilityMissing, WriteToolHarness.Error(await Box()).GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LocationBox_OutsideScratchApprovalCoversCornersAndNoDeviceRenames(bool grant)
    {
        var channel = new FakeApprovalChannel("eplan", grant ? ApprovalDecision.Granted : ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        CallToolResult result = await Box(dryRun: false, harness: harness);
        Assert.Contains("corners (60, 140) to (100, 180); dashed; affected devices: none", Assert.Single(Assert.Single(channel.Questions).Plan.Changes).NewValue, StringComparison.Ordinal);
        if (grant)
        { Assert.Equal("committed", WriteToolHarness.Structured(result).GetProperty("execution").GetString()); }
        else
        { Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(result).GetProperty("reason").GetString()); Assert.Empty(_placement.Calls); }
    }

    [Theory]
    [InlineData("device")]
    [InlineData("box")]
    [InlineData("bounds")]
    [InlineData("symbol")]
    [InlineData("objects")]
    public async Task LocationBox_ApprovalStateChangesInvalidateThePlanBeforeWriting(string change)
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Granted)
        {
            Wait = () =>
            {
                switch (change)
                {
                    case "device":
                        _store.Names.Add("=F+CHANGED-K1");
                        break;
                    case "box":
                        _store.Page.LocationBoxStructures.Add("+OTHER");
                        break;
                    case "bounds":
                        _store.Page.Width = 400;
                        break;
                    case "symbol":
                        _store.Symbols[1].ConnectionPoints.Add(new() { Location = [0, 4] });
                        break;
                    case "objects":
                        _store.Page.Objects.Add("other");
                        break;
                }
            }
        };
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        JsonElement error = WriteToolHarness.Error(await Box(dryRun: false, harness: harness));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_placement.Calls);
    }

    [Fact]
    public async Task LocationBox_RecoveredCommitReturnsPredictionsAndPointsToPageDrawings()
    {
        _harness.Bridge.WriteRecovered = true;
        JsonElement result = WriteToolHarness.Structured(await Box(dryRun: false));
        Assert.True(result.GetProperty("recovered").GetBoolean());
        Assert.Equal("+P1", result.GetProperty("locationBox").GetProperty("structure").GetString());
        Assert.Contains("eplan_page_get", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Empty(result.GetProperty("locationBox").GetProperty("affectedDevices").EnumerateArray());
    }

    [Fact]
    public void LocationBox_ChangedRequestedCornersInvalidateThePlan()
    {
        DevicePlaceArgs args = BoxArgs();
        args.Fingerprint = Plan(args).Fingerprint;
        args.CornerX = 104;
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => _harness.Bridge.Handlers[OperationNames.DevicePlaceExecute](args));
        Assert.Equal(ErrorReason.PlanStale, error.Error.Reason);
        Assert.Empty(_placement.Calls);
    }

    private Task<CallToolResult> Box(bool dryRun = true, string structure = "+P1", WriteToolHarness? harness = null) =>
        DeviceWriteTools.DevicePlaceAsync((harness ?? _harness).Pipeline, "Line1", Page, 60, 140, location_box: structure, corner_x: 100, corner_y: 180, dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);

    private static DevicePlaceArgs BoxArgs() => new() { Project = "Line1", Page = Page, LocationBox = "+P1", X = 60, Y = 140, CornerX = 100, CornerY = 180 };
}
