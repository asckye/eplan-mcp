using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_macro_insert through the real write pipeline against a fake Bridge (docs/tools/eplan_macro_insert.md, sections
/// 3, 4 and 7). The plan is S10's macro on a new page; the commit reads back what S10 read after numbering (A14).
/// </summary>
[Trait("Size", "Medium")]
public sealed class MacroInsertToolTests : IDisposable
{
    private const string Macro = "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema";
    private const string Page = "=GAA+A1&EFS1/60";

    private readonly WriteToolHarness _harness = new(OutsideScratchWrites.Deny);

    public MacroInsertToolTests()
    {
        _harness.Bridge.Handlers[OperationNames.MacroInsertPlan] = _ =>
        {
            WritePlan plan = _harness.Plan([new PlannedChange { Action = "create", Kind = "macro", Name = Page, NewValue = "Contactor_Feedback_Control.ema at (40, 280), numbering number, 3 devices" }]);
            plan.Details = new Dictionary<string, string>
            {
                ["macro"] = Macro,
                ["deviceCount"] = "3",
                ["devices"] = "+-WG6\t-WG6\tCable\n+-XG8:?\t-XG8\tTerminal\n=+A1-QA1\t\tNoContact",
                ["objectCount"] = "28",
                ["pageSizeKnown"] = "false",
            };
            return plan;
        };
        _harness.Bridge.Handlers[OperationNames.MacroInsertExecute] = _ => new MacroInsertData
        {
            Project = "Line1",
            Page = Page,
            Macro = Macro,
            Devices =
            [
                new PageDevice { Tag = "+A1-WG7", VisibleTag = "-WG7", Category = "Cable" },
                new PageDevice { Tag = "+A1-XG9:?1", VisibleTag = "-XG9", Category = "Terminal" },
                new PageDevice { Tag = "=GAA+A1-?QA1", VisibleTag = "-?QA1", Category = "NoContact" },
            ],
            DeviceCount = 3,
            ObjectCount = 28,
            PageSizeKnown = true,
        };
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task OmittedDryRun_PreviewsWithoutApprovalAndReturnsPlan()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");
        JsonElement data = WriteToolHarness.Structured(await MacroWriteTools.MacroInsertAsync(
            _harness.Pipeline, _harness.Bridge, "Line1", Page, 40, 280, Macro,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("not_started", data.GetProperty("execution").GetString());
        Assert.True(data.GetProperty("dryRun").GetBoolean());
        Assert.Equal("f1", data.GetProperty("fingerprint").GetString());
        Assert.Contains("dry_run: false", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MacroBox_PassesUnchangedThroughPlanAndExecution(bool retain)
    {
        await MacroWriteTools.MacroInsertAsync(_harness.Pipeline, _harness.Bridge, "Line1", Page, 0, 292, Macro,
            dry_run: false, macro_box: retain, cancellationToken: TestContext.Current.CancellationToken);
        MacroInsertArgs[] args = [.. _harness.Bridge.Calls.Where(c => c.Operation is OperationNames.MacroInsertPlan or OperationNames.MacroInsertExecute)
            .Select(c => Assert.IsType<MacroInsertArgs>(c.Args))];
        Assert.Equal(2, args.Length);
        Assert.All(args, a => Assert.Equal(retain, a.MacroBox));
    }

    [Fact]
    public async Task Preview_IncompleteLayoutWarningIsPreserved()
    {
        var original = _harness.Bridge.Handlers[OperationNames.MacroInsertPlan];
        _harness.Bridge.Handlers[OperationNames.MacroInsertPlan] = args =>
        {
            var plan = (WritePlan)original(args);
            plan.Details!["layoutWarnings"] = "Layout unverified: page geometry is unavailable.";
            return plan;
        };
        JsonElement data = WriteToolHarness.Structured(await Insert(dryRun: true));
        Assert.Contains("Layout unverified", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartOverview_PassesBothVariantsThroughPlanAndExecution(bool dryRun)
    {
        _harness.Bridge.Handlers[OperationNames.MacroInsertExecute] = _ => new MacroInsertData
        {
            Project = "Line1",
            Page = Page,
            Macro = Macro,
            PageSizeKnown = true,
            ArticleReferences = [new() { Tag = "-K1\u0001", PartNumber = "PXC.2861344", Variant = "1", Count = 1 }],
        };
        JsonElement data = WriteToolHarness.Structured(await MacroWriteTools.MacroInsertAsync(
            _harness.Pipeline, _harness.Bridge, "Line1", Page, 40, 280, variant: 2,
            dry_run: dryRun, representation: "Overview", part: "PXC.2861344", part_variant: "1",
            cancellationToken: TestContext.Current.CancellationToken));

        foreach (var call in _harness.Bridge.Calls.Where(c => c.Operation is OperationNames.MacroInsertPlan or OperationNames.MacroInsertExecute))
        {
            var args = Assert.IsType<MacroInsertArgs>(call.Args);
            Assert.Null(args.Macro);
            Assert.Equal(("PXC.2861344", "1", 2, "Overview"), (args.Part, args.PartVariant, args.Variant, args.Representation));
        }
        if (dryRun)
        { Assert.Empty(_harness.Bridge.Writes); }
        else
        {
            JsonElement reference = Assert.Single(data.GetProperty("articleReferences").EnumerateArray());
            Assert.Equal(("-K1", "PXC.2861344", "1", 1), (reference.GetProperty("tag").GetString(), reference.GetProperty("partNumber").GetString(), reference.GetProperty("variant").GetString(), reference.GetProperty("count").GetInt32()));
            Assert.Single(_harness.Bridge.Writes);
        }
    }

    [Fact]
    public async Task ADryRun_ShowsTheMacroDevicesAndHowTheirTagsChange()
    {
        CallToolResult result = await Insert(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, Page, Macro, 28), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("page").GetString(), data.GetProperty("macro").GetString(), data.GetProperty("objectCount").GetInt32()));
        Assert.Equal(["+-WG6", "+-XG8:?", "=+A1-QA1"], data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("devices")[2].GetProperty("visibleTag").ValueKind);
        string hint = data.GetProperty("hint").GetString()!;
        Assert.Contains("placeholders", hint, StringComparison.Ordinal);
        Assert.Contains("only x >= 0 and y >= 0 were checked", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be numbered", hint, StringComparison.Ordinal);
        Assert.Equal("Dry run: would insert a macro with 3 devices. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task ACommit_ReturnsTheDevicesAsReadBack_AndPointsToTheQuestionMarks()
    {
        CallToolResult result = await Insert();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_macro_insert"), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString()));
        Assert.Equal(["+A1-WG7", "+A1-XG9:?1", "=GAA+A1-?QA1"], data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()));
        Assert.Equal("Tags with '?' could not be numbered (a contact without its coil, a terminal without a number): check them in EPLAN.", data.GetProperty("hint").GetString());
        Assert.Equal("Inserted a macro with 3 devices; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (MacroInsertArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.MacroInsertExecute).Args!;
        Assert.Equal((Page, Macro, 40.0, 280.0, 2, "keep", "f1"), (executed.Page, executed.Macro, executed.X, executed.Y, executed.Variant, executed.Numbering, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task ACommitWithMoreDevicesThanListed_SaysHowMany()
    {
        _harness.Bridge.Handlers[OperationNames.MacroInsertExecute] = _ => new MacroInsertData { Project = "Line1", Page = Page, Macro = Macro, Devices = [new PageDevice { Tag = "+A1-XG1" }], DeviceCount = 60, PageSizeKnown = true };

        CallToolResult result = await Insert();

        Assert.Equal("Only the first 1 of 60 devices are listed.", WriteToolHarness.Structured(result).GetProperty("hint").GetString());
        Assert.Equal("Inserted a macro with 60 devices; Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
    }

    [Fact]
    public async Task ARecoveredCommit_ShowsTheMacroDevicesAndPointsToPageGet()
    {
        _harness.Bridge.WriteRecovered = true;

        JsonElement data = WriteToolHarness.Structured(await Insert());

        Assert.True(data.GetProperty("recovered").GetBoolean());
        Assert.Equal("+-WG6", data.GetProperty("devices")[0].GetProperty("tag").GetString());
        Assert.Contains("eplan_page_get", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErrorReason.AlreadyExists, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.InvalidArgument, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    public async Task AnExecutionError_ReachesTheModelWithItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution, Metadata = new() { ["tags"] = "+A1-WG6" } };

        JsonElement error = WriteToolHarness.Error(await Insert());

        Assert.Equal((reason, execution, "+A1-WG6"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("tags").GetString()));
    }

    [Fact]
    public async Task APlanError_IsNotStarted()
    {
        _harness.Bridge.Handlers[OperationNames.MacroInsertPlan] = _ => throw new BridgeCallException(new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "Invalid arguments: 'page' must be a circuit page (Circuit)." });

        JsonElement error = WriteToolHarness.Error(await Insert());

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Insert());

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task WithoutAProject_TheBridgeIsNotAsked()
    {
        JsonElement error = WriteToolHarness.Error(await MacroWriteTools.MacroInsertAsync(_harness.Pipeline, _harness.Bridge, " ", Page, 40, 280, Macro, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'project' is required", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private Task<CallToolResult> Insert(bool dryRun = false) =>
        MacroWriteTools.MacroInsertAsync(_harness.Pipeline, _harness.Bridge, "Line1", Page, 40, 280, Macro, 2, "keep", dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
