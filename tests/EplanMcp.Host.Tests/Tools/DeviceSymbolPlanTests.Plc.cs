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
    private void PlcSymbol()
    {
        _store.Names.Add("+A1-KF1");
        _store.Symbols.Add(new() { Library = "IEC_symbol", Name = "PLC_DI", Type = "Function", Kind = "function", FunctionCategory = "PLCTerminal", PlcInputDefinition = "1/2", ConnectionPoints = [new() { Location = [0, 0] }] });
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
    public async Task Plc_PreviewListsAddressTextAndExtraRepresentation_ThenCommitsAndRejectsDuplicate()
    {
        PlcSymbol();
        JsonElement preview = WriteToolHarness.Structured(await Plc());
        Assert.Empty(_placement.Calls);
        Assert.Contains(preview.GetProperty("changes").EnumerateArray(), c => c.GetProperty("property").GetString() == "20402" && c.GetProperty("newValue").GetString() == "Start");
        Assert.Contains("parentless PLCTerminal", preview.GetProperty("changes")[0].GetProperty("newValue").GetString(), StringComparison.Ordinal);
        Assert.Equal("+A1-KF1:X10:2", preview.GetProperty("device").GetProperty("tag").GetString());
        JsonElement placed = WriteToolHarness.Structured(await Plc(dryRun: false));
        JsonElement point = placed.GetProperty("plc");
        Assert.Equal(("I0.0", "Start", "Start input"), (point.GetProperty("address").GetString(), point.GetProperty("manualSymbolicAddress").GetString(), point.GetProperty("text").GetString()));
        Assert.Equal(JsonValueKind.Null, point.GetProperty("owner").ValueKind);
        Assert.Equal(JsonValueKind.Null, point.GetProperty("cardReference").ValueKind);
        Assert.Equal(["X10:2"], _placement.Args!.ConnectionDesignations!);
        Assert.Equal(["Begin", "Write", "Readback", "ReleaseLists", "Commit", "CloseUndo", "Dispose"], _placement.Calls);
        Assert.Equal(ErrorReason.AlreadyExists, WriteToolHarness.Error(await Plc(dryRun: false)).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.AlreadyExists, WriteToolHarness.Error(await Plc(point: "DI a.0")).GetProperty("reason").GetString());
        Assert.Equal(0, _store.PartReads);
    }

    [Theory]
    [InlineData("PlcOwner")]
    [InlineData("PlcAddress")]
    [InlineData("PlcSymbol")]
    [InlineData("PlcText")]
    [InlineData("Write")]
    [InlineData("Cancel")]
    public async Task Plc_MismatchFailureOrCancellation_AbortsWithoutAnEmptyUndo(string failAt)
    {
        PlcSymbol();
        _placement.FailAt = failAt;
        JsonElement error = WriteToolHarness.Error(await Plc(dryRun: false));
        Assert.Equal(ExecutionState.RolledBack, error.GetProperty("execution").GetString());
        Assert.Equal(["+A1-KF1"], _store.Names);
        Assert.Empty(_store.Channels);
        Assert.Equal(["ReleaseLists", "Abort", "Dispose"], _placement.Calls.TakeLast(3));
        Assert.DoesNotContain("CloseUndo", _placement.Calls);
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
        Assert.Equal(stale ? ErrorReason.PlanStale : ErrorReason.ApprovalDenied, error.GetProperty("reason").GetString());
        Assert.Contains(Assert.Single(approval.Questions).Plan.Changes, c => c.Property == "20011 (??_??)");
        Assert.Empty(_placement.Calls);
    }

    private Task<CallToolResult> Plc(bool dryRun = true, string point = "X10:2", WriteToolHarness? harness = null) =>
        DeviceWriteTools.DevicePlaceAsync((harness ?? _harness).Pipeline, "Line1", Page, 80, 160, "-KF1", dry_run: dryRun,
            library: "IEC_symbol", symbol: "PLC_DI", symbol_variant: 0, plc_connection_point: point, plc_address: "I0.0", plc_symbolic_address: "Start", plc_text: "Start input", cancellationToken: TestContext.Current.CancellationToken);
}
