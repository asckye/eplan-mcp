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
    private void TerminalSymbol() => _store.Symbols.Add(new() { Library = "IEC_symbol", Name = "X", Type = "Function", Kind = "terminal", Variant = 0 });

    [Fact]
    public async Task Terminal_PreviewCreatesParentFirst_ThenCommitsAndReusesIt()
    {
        TerminalSymbol();
        JsonElement preview = WriteToolHarness.Structured(await Terminal(text: "Control box"));
        Assert.Equal(2, preview.GetProperty("functions").GetInt32());
        Assert.Equal("terminal_strip", preview.GetProperty("changes")[0].GetProperty("kind").GetString());
        Assert.Contains("Control box", preview.GetProperty("changes")[0].GetProperty("newValue").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("terminal").GetProperty("owner").ValueKind);
        Assert.Empty(_placement.Calls);
        Assert.Empty(_store.Names);
        JsonElement placed = WriteToolHarness.Structured(await Terminal(dryRun: false, text: "Control box"));
        Assert.Equal(("committed", "=F+A1-X6", "17"), (placed.GetProperty("execution").GetString(), placed.GetProperty("terminal").GetProperty("owner").GetString(), placed.GetProperty("terminal").GetProperty("sortCode").GetString()));
        Assert.Equal(["Begin", "Write", "Readback", "ReleaseLists", "Commit", "CloseUndo", "Dispose"], _placement.Calls);
        Assert.Equal("Control box", _placement.Args!.TerminalStripText);
        Assert.Equal("1", _placement.Args.TerminalNumber);
        JsonElement duplicate = WriteToolHarness.Error(await Terminal(dryRun: false, strip: "-x6"));
        Assert.Equal(ErrorReason.AlreadyExists, duplicate.GetProperty("reason").GetString());
        JsonElement second = WriteToolHarness.Structured(await Terminal(dryRun: false, strip: "-x6", number: "2"));
        Assert.False(second.GetProperty("terminal").GetProperty("stripCreated").GetBoolean());
        Assert.Equal("=F+A1-X6:2", second.GetProperty("device").GetProperty("tag").GetString());
        Assert.Equal(1, second.GetProperty("functions").GetInt32());
        Assert.Equal(0, _store.PartReads);
        Assert.All(_harness.Bridge.Calls, c => Assert.Contains(c.Operation, new[] { OperationNames.DevicePlacePlan, OperationNames.DevicePlaceExecute }));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Number")]
    [InlineData("Position")]
    [InlineData("Write")]
    public async Task Terminal_MismatchOrFailure_RestoresParentAndTerminalWithoutEmptyUndo(string failAt)
    {
        TerminalSymbol();
        _placement.FailAt = failAt;
        JsonElement error = WriteToolHarness.Error(await Terminal(dryRun: false));
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_store.Names);
        Assert.Equal(["page:1"], _store.Page.Objects);
        Assert.Equal(["ReleaseLists", "Abort", "Dispose"], _placement.Calls.TakeLast(3));
        Assert.DoesNotContain("CloseUndo", _placement.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Terminal_ApprovalListsParentTextAndChild_AndRespectsDecision(bool grant)
    {
        TerminalSymbol();
        var channel = new FakeApprovalChannel("eplan", grant ? ApprovalDecision.Granted : ApprovalDecision.Denied);
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        CallToolResult result = await Terminal(dryRun: false, text: "Control box", harness: harness);
        Assert.Equal(2, Assert.Single(channel.Questions).Plan.Changes.Count);
        if (grant)
        { Assert.Equal("committed", WriteToolHarness.Structured(result).GetProperty("execution").GetString()); }
        else
        { Assert.Equal(ErrorReason.ApprovalDenied, WriteToolHarness.Error(result).GetProperty("reason").GetString()); Assert.Empty(_placement.Calls); }
    }

    [Fact]
    public async Task Terminal_AnotherPageAddsAChildDuringApproval_RefusesStalePlan()
    {
        TerminalSymbol();
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Granted) { Wait = () => _store.Names.Add("=F+A1-X6:9") };
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        Bind(harness);
        JsonElement error = WriteToolHarness.Error(await Terminal(dryRun: false, harness: harness));
        Assert.Equal(ErrorReason.PlanStale, error.GetProperty("reason").GetString());
        Assert.Empty(_placement.Calls);
    }

    [Fact]
    public async Task Terminal_ExistingParentTextCannotBeOverwritten_AndOtherVariantsMustExist()
    {
        TerminalSymbol();
        _store.Names.Add("=F+A1-X6");
        Assert.Equal(ErrorReason.InvalidArgument, WriteToolHarness.Error(await Terminal(text: "New text")).GetProperty("reason").GetString());
        Assert.Equal(ErrorReason.SymbolNotFound, WriteToolHarness.Error(await Terminal(variant: 9)).GetProperty("reason").GetString());
        Assert.Empty(_placement.Calls);
    }

    [Theory]
    [InlineData("number")]
    [InlineData("text")]
    [InlineData("position")]
    public void Terminal_ChangedRequestCannotReuseAnApprovedFingerprint(string field)
    {
        TerminalSymbol();
        var args = new DevicePlaceArgs { Project = "Line1", Page = Page, TerminalStrip = "-X6", TerminalNumber = "1", TerminalStripText = "Control box", X = 80, Y = 160 };
        args.Fingerprint = Plan(args).Fingerprint;
        switch (field)
        {
            case "number":
                args.TerminalNumber = "2";
                break;
            case "text":
                args.TerminalStripText = "Another box";
                break;
            case "position":
                args.X = 84;
                break;
        }
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => _harness.Bridge.Handlers[OperationNames.DevicePlaceExecute](args));
        Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Error.Reason, error.Error.Execution));
        Assert.Empty(_placement.Calls);
    }

    private Task<CallToolResult> Terminal(bool dryRun = true, string strip = "-X6", string number = "1", string? text = null, int variant = 0, WriteToolHarness? harness = null) =>
        DeviceWriteTools.DevicePlaceAsync((harness ?? _harness).Pipeline, (harness ?? _harness).Bridge, "Line1", Page, 80, 160, dry_run: dryRun,
            terminal_strip: strip, terminal_number: number, terminal_strip_text: text, symbol_variant: variant, cancellationToken: TestContext.Current.CancellationToken);
}
