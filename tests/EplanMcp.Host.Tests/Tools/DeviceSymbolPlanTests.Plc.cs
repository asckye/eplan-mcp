using System.Text.Json;
using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class DeviceSymbolPlanTests
{
    private static readonly int[] _plcDisplayIds = [20400, 20403, 20011, 20301];
    private void PlcSymbol()
    {
        _store.Names.Add("+A1-KF1");
        _store.Symbols.Add(new() { Library = "IEC_symbol", Name = "PLC_DI", Type = "Function", Kind = "function", FunctionCategory = "PLCTerminal", PlcInputDefinition = "1/2", ConnectionPoints = [new() { Location = [0, 0] }] });
    }

    [Fact]
    public void Plc_ChangedPlugDesignation_ChangesApprovalAndFingerprint()
    {
        PlcSymbol();
        DevicePlaceArgs args = Args();
        args.Tag = "-KF1";
        args.Symbol = "PLC_DI";
        args.PlcConnectionPoint = "1";
        args.PlcAddress = "I0.0";
        args.PlcSymbolicAddress = "Start";
        args.PlcText = "Start input";
        args.PlcPlugDesignation = "-X10";
        WritePlan first = Plan(args);
        Assert.Contains("+A1-KF1:1", first.Changes[0].NewValue, StringComparison.Ordinal);
        args.PlcPlugDesignation = "-X11";
        WritePlan second = Plan(args);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Contains(first.Changes, c => c.Property == "20406" && c.NewValue == "-X10");
        Assert.Contains(second.Changes, c => c.Property == "20406" && c.NewValue == "-X11");
    }

    [Theory]
    [InlineData("NoContact", "13", "14")]
    [InlineData("NcContact", "11", "12")]
    public async Task Contact_PreviewAndExecutionPreserveIndexedDesignations(string category, string first, string second)
    {
        _store.Symbols[0].FunctionCategory = category;
        JsonElement preview = WriteToolHarness.Structured(await Place(designations: [first, second]));
        Assert.Equal(["20022[1]", "20022[2]"], preview.GetProperty("changes").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "pin").Select(c => c.GetProperty("property").GetString()));
        JsonElement result = WriteToolHarness.Structured(await Place(dryRun: false, designations: [first, second]));
        Assert.Equal(category, result.GetProperty("device").GetProperty("category").GetString());
        Assert.Equal([first, second], _placement.Args!.ConnectionDesignations!);
        Assert.Equal(["Commit", "CloseUndo", "Dispose"], _placement.Calls.TakeLast(3));
    }

    [Fact]
    public void Plc_PlanReusesTheProjectNameScanForParentResolutionAndDuplicateChecks()
    {
        PlcSymbol();
        DevicePlaceArgs args = Args();
        args.Tag = "-KF1";
        args.Symbol = "PLC_DI";
        args.PlcConnectionPoint = "DI a.0";
        args.PlcAddress = "I0.0";
        args.PlcSymbolicAddress = "Start";
        args.PlcText = "Start input";
        Plan(args);
        Assert.Equal(1, _store.NameReads);
    }

    [Fact]
    public void Plc_RejectsAPlainSymbolCallAndInconsistentDesignations()
    {
        PlcSymbol();
        DevicePlaceArgs args = Args();
        args.Tag = "-KF2";
        args.Symbol = "PLC_DI";
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        args.Tag = "-KF1";
        args.PlcConnectionPoint = "DI a.0";
        args.PlcAddress = "I0.0";
        args.PlcSymbolicAddress = "Start";
        args.PlcText = "Start input";
        args.ConnectionDesignations = ["wrong"];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        args.ConnectionDesignations = ["DI a.0"];
        Assert.Contains(Plan(args).Changes, c => c.Property == "20022[1]" && c.NewValue == "DI a.0");
    }

    [Fact]
    public void Plc_ConnectionDescription_IsOnePerPinAndPlannedAs20023()
    {
        // Customer redraw: the PLC table's first row (20029) shows the description written to 20023.
        PlcSymbol();
        DevicePlaceArgs args = Args();
        args.Tag = "-KF1";
        args.Symbol = "PLC_DI";
        args.PlcConnectionPoint = "2";
        args.PlcAddress = "I0.0";
        args.PlcSymbolicAddress = "Start";
        args.PlcText = "Start input";
        args.ConnectionDescriptions = ["DI a.0", "extra"];
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Plan(args)).Error.Reason);
        args.ConnectionDescriptions = ["DI a.0"];
        Assert.Contains(Plan(args).Changes, c => c.Property == "20023[1]" && c.NewValue == "DI a.0");
    }

    [Fact]
    public async Task Plc_PreviewListsAddressTextAndExtraRepresentation_ThenCommitsAndRejectsDuplicate()
    {
        PlcSymbol();
        JsonElement preview = WriteToolHarness.Structured(await Plc());
        Assert.Empty(_placement.Calls);
        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("property").GetString() == "20400" && c.GetProperty("newValue").GetString() == "I0.0");
        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("property").GetString() == "20402" && c.GetProperty("newValue").GetString() == "Start");
        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("property").GetString() == "20011 (??_??)" && c.GetProperty("newValue").GetString() == "Start input");
        Assert.Contains("existing card +A1-KF1", preview.GetProperty("changes")[0].GetProperty("newValue").GetString(), StringComparison.Ordinal);
        Assert.Equal("+A1-KF1:X10:2", preview.GetProperty("device").GetProperty("tag").GetString());
        JsonElement placed = WriteToolHarness.Structured(await Plc(dryRun: false));
        JsonElement point = placed.GetProperty("plc");
        Assert.Equal(("I0.0", "Start", "Start input"), (point.GetProperty("address").GetString(), point.GetProperty("manualSymbolicAddress").GetString(), point.GetProperty("text").GetString()));
        Assert.Equal(JsonValueKind.Null, point.GetProperty("owner").ValueKind);
        Assert.Equal(JsonValueKind.Null, point.GetProperty("cardReference").ValueKind);
        Assert.Equal(["X10:2"], _placement.Args!.ConnectionDesignations!);
        Assert.Equal(("I0.0", "Start", "Start input"), (_placement.Args.PlcAddress, _placement.Args.PlcSymbolicAddress, _placement.Args.PlcText));
        Assert.Null(_placement.Args.DisplayProperties);
        Assert.Empty(_placement.Displays);
        Assert.Equal(ExecutionState.Committed, placed.GetProperty("execution").GetString());
        Assert.Equal(["Begin", "Write", "Readback", "ReleaseLists", "Commit", "CloseUndo", "Dispose", "CommittedReadback"], _placement.Calls);
        Assert.Equal(ErrorReason.AlreadyExists, WriteToolHarness.Error(await Plc(dryRun: false, address: "I0.1")).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.AlreadyExists, WriteToolHarness.Error(await Plc(point: "DI a.0")).GetProperty("reason").GetString());
        Assert.Single(_store.Channels);
        Assert.Single(_harness.Bridge.Writes);
        Assert.Equal(0, _store.PartReads);
    }

    [Theory]
    [InlineData("=GAA+A1-D3KF3", null)]
    [InlineData("=GAA+A1-D3KF3", "-X10")]
    [InlineData("=GAA+A1-D3KF5", "-X10")]
    public async Task Plc_D3PostCommitNameLookupMiss_WarnsWithoutSuspendingWrites(string tag, string? plug)
    {
        PlcSymbol();
        _store.Page.Name = "=GAA+A1&EFS1/3";
        _store.Names[0] = tag;
        _store.Symbols[^1].Name = "PLC_CBOX";
        JsonElement[] displays = [.. _plcDisplayIds.Select((id, row) => JsonSerializer.SerializeToElement(new { property_id = id, offset_x_mm = 0, offset_y_mm = -8 - 8 * row }))];
        JsonElement preview = WriteToolHarness.Structured(await PlaceD3(dryRun: true));
        if (plug is not null)
        { Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("property").GetString() == "20406" && c.GetProperty("newValue").GetString() == plug); }
        Assert.Empty(_placement.Calls);
        _placement.FailAt = "CommittedNameLookup";
        JsonElement result = WriteToolHarness.Structured(await PlaceD3());
        Assert.Equal(ExecutionState.Committed, result.GetProperty("execution").GetString());
        Assert.Contains("was committed", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Contains("plug designation", result.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Empty(SuspensionStore.For(_harness.Locations).All());
        Assert.Equal(plug, _placement.Args!.PlcPlugDesignation);
        Assert.Contains("Commit", _placement.Calls);
        Assert.DoesNotContain("Abort", _placement.Calls);
        _placement.FailAt = null;
        Assert.Equal(ExecutionState.Committed, WriteToolHarness.Structured(await PlaceD3(point: "3", address: "I0.2")).GetProperty("execution").GetString());
        Assert.Equal(2, _harness.Bridge.Writes.Count);

        Task<CallToolResult> PlaceD3(bool dryRun = false, string point = "1", string address = "I0.1") =>
            DeviceWriteTools.DevicePlaceAsync(_harness.Pipeline, _harness.Bridge, "Line1", _store.Page.Name, 120, 176, tag, dry_run: dryRun,
                library: "IEC_symbol", symbol: "PLC_CBOX", symbol_variant: 0, plc_connection_point: point, plc_address: address,
                plc_symbolic_address: "FICTIONAL_DI_1", plc_text: "Fictional D3 input", plc_plug_designation: plug,
                display_properties: displays, cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Plc_PlugAndDisplayCommit_ReturnNativeReadback()
    {
        PlcSymbol();
        JsonElement placed = WriteToolHarness.Structured(await Plc(dryRun: false, plug: "-X10",
            displays: [JsonSerializer.SerializeToElement(new { property_id = 20406, offset_x_mm = 4, offset_y_mm = 4 })]));
        Assert.Equal(ExecutionState.Committed, placed.GetProperty("execution").GetString());
        Assert.Equal("-X10", placed.GetProperty("plc").GetProperty("plugDesignation").GetString());
        Assert.Contains("ReadDisplays", _placement.Calls);
        Assert.Equal("CommittedReadback", _placement.Calls[^1]);
    }

    [Fact]
    public async Task OrdinarySymbol_PlaceReportsSuccessWithoutDisplay_AbortsWithoutCommit()
    {
        _placement.FailAt = "DisplayMissing";
        JsonElement error = WriteToolHarness.Error(await Place(dryRun: false));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_store.Names);
        Assert.Empty(_placement.Displays);
        Assert.DoesNotContain("Commit", _placement.Calls);
        Assert.DoesNotContain("CloseUndo", _placement.Calls);
    }

    [Theory]
    [InlineData("PlcOwner")]
    [InlineData("PlcPlug")]
    [InlineData("PlcAddress")]
    [InlineData("PlcSymbol")]
    [InlineData("PlcText")]
    [InlineData("Write")]
    [InlineData("Cancel")]
    public async Task Plc_MismatchFailureOrCancellation_AbortsWithoutAnEmptyUndo(string failAt)
    {
        PlcSymbol();
        _placement.FailAt = failAt;
        JsonElement error = WriteToolHarness.Error(await Plc(dryRun: false, plug: failAt == "PlcPlug" ? "-X10" : null));
        Assert.Equal((failAt == "Cancel" ? ErrorReason.Cancelled : ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Equal(["+A1-KF1"], _store.Names);
        Assert.Equal(["page:1"], _store.Page.Objects);
        Assert.Empty(_store.Channels);
        Assert.Equal(["ReleaseLists", "Abort", "Dispose"], _placement.Calls.TakeLast(3));
        Assert.DoesNotContain("CloseUndo", _placement.Calls);
        Assert.DoesNotContain("Commit", _placement.Calls);
    }

    [Fact]
    public async Task Plc_RequiresAnExistingNativeCardAndVerifiedDefinition()
    {
        PlcSymbol();
        _store.Names.Clear();
        Assert.Equal(ErrorReason.DeviceNotFound, WriteToolHarness.Error(await Plc()).GetProperty("reason").GetString());
        _store.Names.Add("+A1-KF1");
        _store.Symbols[^1].PlcInputDefinition = null;
        Assert.Equal(ErrorReason.CapabilityMissing, WriteToolHarness.Error(await Plc()).GetProperty("reason").GetString());
        Assert.Empty(_placement.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plc_ApprovalDenialOrChangedCardState_PerformsNoWrites(bool stale)
    {
        PlcSymbol();
        var approval = new FakeApprovalChannel("eplan", stale ? ApprovalDecision.Granted : ApprovalDecision.Denied)
        { Wait = () => { if (stale) { _store.Channels.Add(new() { Name = "+A1-KF1:9", Address = "I1.0" }); } } };
        using var harness = new WriteToolHarness(approval) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        JsonElement error = WriteToolHarness.Error(await Plc(dryRun: false, harness: harness));
        Assert.Equal((stale ? ErrorReason.PlanStale : ErrorReason.ApprovalDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        IReadOnlyList<PlannedChange> changes = Assert.Single(approval.Questions).Plan.Changes;
        Assert.Contains(changes, c => c.Property == "20400" && c.NewValue == "I0.0");
        Assert.Contains(changes, c => c.Property == "20402" && c.NewValue == "Start");
        Assert.Contains(changes, c => c.Property == "20011 (??_??)" && c.NewValue == "Start input");
        Assert.Empty(_placement.Calls);
    }

    private Task<CallToolResult> Plc(bool dryRun = true, string point = "X10:2", WriteToolHarness? harness = null, JsonElement[]? displays = null, string address = "I0.0", string? plug = null) =>
        DeviceWriteTools.DevicePlaceAsync((harness ?? _harness).Pipeline, (harness ?? _harness).Bridge, "Line1", Page, 80, 160, "-KF1", dry_run: dryRun,
            library: "IEC_symbol", symbol: "PLC_DI", symbol_variant: 0, plc_connection_point: point, plc_address: address, plc_symbolic_address: "Start", plc_text: "Start input",
            display_properties: displays, plc_plug_designation: plug,
            cancellationToken: TestContext.Current.CancellationToken);
}
