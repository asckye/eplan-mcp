using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// eplan_placeholder_apply through the real write pipeline against a fake Bridge (docs/tools/eplan_placeholder_apply.md,
/// sections 3, 4 and 7): the dry run from the plan's details, the commit with the current values before and after, the
/// approval form with the values, and errors with their execution state.
/// </summary>
[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PlaceholderApplyToolTests : IDisposable
{
    private const string Page = "=TM1+A1&EFS1/1";

    private readonly FakeApprovalChannel _dialog = new("eplan", ApprovalDecision.Granted);
    private readonly WriteToolHarness _harness;

    public PlaceholderApplyToolTests()
    {
        _harness = new(OutsideScratchWrites.Deny, _dialog);
        _harness.Bridge.Handlers[OperationNames.PlaceholderApplyPlan] = _ => Planned(_harness);
        _harness.Bridge.Handlers[OperationNames.PlaceholderApplyExecute] = _ => Applied(changed: true);
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ADryRun_ShowsTheValuesWithoutCurrentValues_AndChangesNothing()
    {
        CallToolResult result = await Apply(dryRun: true);

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("not_started", true, "mA"), (data.GetProperty("execution").GetString(), data.GetProperty("dryRun").GetBoolean(), data.GetProperty("valueSet").GetString()));
        Assert.Equal(("", Page), (data.GetProperty("placeholder").GetProperty("name").GetString(), data.GetProperty("placeholder").GetProperty("page").GetString()));
        Assert.Equal([("T1", "4-20 mA"), ("T2", null)], data.GetProperty("values").EnumerateArray().Select(v => (v.GetProperty("variable").GetString(), v.GetProperty("text").GetString())));
        Assert.False(data.TryGetProperty("currentValuesBefore", out _));
        Assert.False(data.TryGetProperty("currentValuesAfter", out _));
        Assert.False(data.TryGetProperty("currentValuesChanged", out _));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("undo").ValueKind);
        Assert.Equal("Dry run: would apply the value set to the placeholder. Nothing was changed.", WriteToolHarness.Text(result, 0));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task McpDefaultAndTruePreview_FalsePreservesExecution()
    {
        Func<object?, object> originalPlan = _harness.Bridge.Handlers[OperationNames.PlaceholderApplyPlan];
        _harness.Bridge.Handlers[OperationNames.PlaceholderApplyPlan] = input =>
        {
            WritePlan result = Assert.IsType<WritePlan>(originalPlan(input));
            foreach (PlannedChange change in result.Changes)
            {
                change.Name += "\u0007\u202e";
                change.Property = change.Property is null ? null : change.Property + "\u0007\u202e";
                change.OldValue = change.OldValue is null ? null : change.OldValue + "\u0007\u202e";
                change.NewValue = change.NewValue is null ? null : change.NewValue + "\u0007\u202e";
            }

            return result;
        };
        await PreviewProtocolHarness.RunAsync(_harness, async client =>
        {
            Tool definition = Assert.Single(await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), item => item.Name == PlaceholderWriteTools.ApplyTool).ProtocolTool;
            Assert.True(definition.InputSchema.GetProperty("properties").GetProperty("dry_run").GetProperty("default").GetBoolean());
            JsonElement? omitted = null;
            foreach (bool? dryRun in new bool?[] { null, true, false })
            {
                _harness.Bridge.Calls.Clear();
                _harness.Bridge.Writes.Clear();
                _dialog.Questions.Clear();
                var args = new Dictionary<string, object?> { ["project"] = "Line1", ["page"] = Page, ["value_set"] = "mA", ["placeholder"] = string.Empty };
                if (dryRun.HasValue)
                {
                    args["dry_run"] = dryRun.Value;
                }

                JsonElement actual = WriteToolHarness.Structured(await client.CallToolAsync(PlaceholderWriteTools.ApplyTool, args, cancellationToken: TestContext.Current.CancellationToken));
                bool preview = dryRun != false;
                Assert.Equal(preview, actual.GetProperty("dryRun").GetBoolean());
                Assert.Equal(preview ? ExecutionState.NotStarted : ExecutionState.Committed, actual.GetProperty("execution").GetString());
                Assert.Equal("f1", actual.GetProperty("fingerprint").GetString());
                Assert.NotEmpty(actual.GetProperty("changes").EnumerateArray());
                Assert.InRange(ResultShaper.ResultTokens(actual.GetRawText()), 1, ResultShaper.DefaultTokenBudget);
                foreach (JsonElement change in actual.GetProperty("changes").EnumerateArray())
                {
                    foreach (JsonProperty field in change.EnumerateObject())
                    {
                        if (field.Value.ValueKind == JsonValueKind.String)
                        {
                            Assert.DoesNotContain('\u0007', field.Value.GetString()!);
                            Assert.DoesNotContain('\u202e', field.Value.GetString()!);
                        }
                    }
                }

                if (preview)
                {
                    Assert.Empty(_dialog.Questions);
                    Assert.False(actual.TryGetProperty("currentValuesBefore", out _));
                    Assert.False(actual.TryGetProperty("currentValuesAfter", out _));
                    Assert.False(actual.TryGetProperty("currentValuesChanged", out _));
                    Assert.Empty(_harness.Bridge.Writes);
                    Assert.DoesNotContain(_harness.Bridge.Calls, call => call.Operation == OperationNames.PlaceholderApplyExecute);
                    Assert.False(Directory.Exists(_harness.Locations.Audit));
                    Assert.False(Directory.Exists(_harness.Locations.Backups));
                    Assert.Contains("dry_run: false", actual.GetProperty("hint").GetString(), StringComparison.Ordinal);
                    if (omitted.HasValue)
                    {
                        Assert.Equal(omitted.Value.GetRawText(), actual.GetRawText());
                    }

                    omitted = actual.Clone();
                }
                else
                {
                    Assert.Empty(_dialog.Questions);
                    Assert.Single(_harness.Bridge.Writes);
                    Assert.Contains(_harness.Bridge.Calls, call => call.Operation == OperationNames.PlaceholderApplyExecute);
                    Assert.Equal(["4-20 mA", "20 mA"], actual.GetProperty("currentValuesAfter").EnumerateArray().Select(value => value.GetProperty("text").GetString()));
                    Assert.True(actual.GetProperty("currentValuesChanged").GetBoolean());
                    Assert.Equal("AI: eplan_placeholder_apply", actual.GetProperty("undo").GetString());
                }
            }
        });
    }

    [Fact]
    public async Task ACommit_ReturnsTheCurrentValuesBeforeAndAfter()
    {
        CallToolResult result = await Apply();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal(("committed", "AI: eplan_placeholder_apply", true), (data.GetProperty("execution").GetString(), data.GetProperty("undo").GetString(), data.GetProperty("currentValuesChanged").GetBoolean()));
        Assert.Equal(["0-10 V", "10 V"], data.GetProperty("currentValuesBefore").EnumerateArray().Select(v => v.GetProperty("text").GetString()));
        Assert.Equal(["4-20 mA", "20 mA"], data.GetProperty("currentValuesAfter").EnumerateArray().Select(v => v.GetProperty("text").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("hint").ValueKind);
        Assert.Equal("Applied the value set to the placeholder; its current values changed. Ctrl+Z in EPLAN undoes this step.", WriteToolHarness.Text(result, 0));
        var executed = (PlaceholderApplyArgs)_harness.Bridge.Calls.Last(c => c.Operation == OperationNames.PlaceholderApplyExecute).Args!;
        Assert.Equal((Page, "", "mA", "f1"), (executed.Page, executed.Placeholder, executed.ValueSet, executed.Fingerprint));
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(_harness.Bridge.Writes).Limit);
    }

    [Fact]
    public async Task UnchangedCurrentValues_AreReported_NotRefused()
    {
        _harness.Bridge.Handlers[OperationNames.PlaceholderApplyExecute] = _ => Applied(changed: false);

        CallToolResult result = await Apply();

        JsonElement data = WriteToolHarness.Structured(result);
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.StartsWith("The current values did not change", data.GetProperty("hint").GetString(), StringComparison.Ordinal);
        Assert.Contains("did not change", WriteToolHarness.Text(result, 0), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErrorReason.InvalidArgument, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.EplanOperationFailed, ExecutionState.RolledBack)]
    [InlineData(ErrorReason.PlanStale, ExecutionState.NotStarted)]
    public async Task AnExecutionError_ReachesTheModelWithItsExecutionState(string reason, string execution)
    {
        _harness.Bridge.WriteFailure = new BridgeError { Reason = reason, Message = "m", Execution = execution, Metadata = new() { ["problem"] = "its value sets changed" } };

        JsonElement error = WriteToolHarness.Error(await Apply());

        Assert.Equal((reason, execution, "its value sets changed"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString(), error.GetProperty("metadata").GetProperty("problem").GetString()));
    }

    [Theory]
    [InlineData(ErrorReason.InvalidArgument)]
    [InlineData(ErrorReason.AmbiguousName)]
    [InlineData(ErrorReason.PageNotFound)]
    public async Task APlanError_IsNotStarted(string reason)
    {
        _harness.Bridge.Handlers[OperationNames.PlaceholderApplyPlan] = _ => throw new BridgeCallException(new BridgeError { Reason = reason, Message = "m" });

        JsonElement error = WriteToolHarness.Error(await Apply());

        Assert.Equal((reason, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_IsPolicyDenied()
    {
        _harness.ProjectPath = Path.Combine(_harness.Locations.Root, "Projects", "Line1");

        JsonElement error = WriteToolHarness.Error(await Apply());

        Assert.Equal(("POLICY_DENIED", "not_started"), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
        Assert.Empty(_harness.Bridge.Writes);
    }

    [Fact]
    public async Task OutsideScratch_WithApproval_AsksWithTheValueSetAndItsValues()
    {
        var channel = new FakeApprovalChannel("eplan", ApprovalDecision.Granted);
        using var harness = new WriteToolHarness(channel) { ProjectPath = @"D:\Projects\Line1" };
        harness.Bridge.Handlers[OperationNames.PlaceholderApplyPlan] = _ => Planned(harness);
        harness.Bridge.Handlers[OperationNames.PlaceholderApplyExecute] = _ => Applied(changed: true);

        JsonElement data = WriteToolHarness.Structured(await PlaceholderWriteTools.PlaceholderApplyAsync(harness.Pipeline, "Line1", Page, "mA", dry_run: false, cancellationToken: TestContext.Current.CancellationToken));

        ApprovalQuestion question = Assert.Single(channel.Questions);
        WritePlan plan = question.Plan;
        Assert.Equal("committed", data.GetProperty("execution").GetString());
        Assert.False(plan.Details!.ContainsKey("currentValues"));
        Assert.Equal("apply value set 'mA' to placeholder =TM1+A1&EFS1/1 (no name)", Assert.Single(WritePipeline.ChangeLines(plan)));
        string[] form = [.. ApprovalTexts.For(question, CultureInfo.GetCultureInfo("en-US")).Changes];
        Assert.Equal(["Apply value set mA to placeholder =TM1+A1&EFS1/1 (no name)", string.Empty, "The value set's values:", "T1 = 4-20 mA", "T2 = "], form);
    }

    [Fact]
    public async Task WithoutAProject_TheBridgeIsNotAsked()
    {
        JsonElement error = WriteToolHarness.Error(await PlaceholderWriteTools.PlaceholderApplyAsync(_harness.Pipeline, " ", Page, "mA", dry_run: false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'project' is required", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _harness.Bridge.Invocations);
    }

    private static WritePlan Planned(WriteToolHarness harness)
    {
        WritePlan plan = harness.Plan([new PlannedChange { Action = "update", Kind = "placeholder", Name = Page + " (no name)", Property = "value set", NewValue = "mA" }]);
        plan.Details = new Dictionary<string, string>
        {
            ["page"] = Page,
            ["placeholder"] = string.Empty,
            ["values"] = "T1\t4-20 mA\nT2\t",
            ["assignedObjects"] = "2",
        };
        return plan;
    }

    private static PlaceholderApplyData Applied(bool changed) => new()
    {
        Project = "Line1",
        Page = Page,
        ValueSet = "mA",
        Values = [new PlaceholderValue { Variable = "T1", Text = "4-20 mA" }, new PlaceholderValue { Variable = "T2", Text = "20 mA" }],
        CurrentValuesBefore = [new PlaceholderValue { Text = "0-10 V" }, new PlaceholderValue { Text = "10 V" }],
        CurrentValuesAfter = changed ? [new PlaceholderValue { Text = "4-20 mA" }, new PlaceholderValue { Text = "20 mA" }] : [new PlaceholderValue { Text = "0-10 V" }, new PlaceholderValue { Text = "10 V" }],
        CurrentValuesChanged = changed,
    };

    private Task<CallToolResult> Apply(bool dryRun = false) =>
        PlaceholderWriteTools.PlaceholderApplyAsync(_harness.Pipeline, "Line1", Page, "mA", placeholder: string.Empty, dry_run: dryRun, cancellationToken: TestContext.Current.CancellationToken);
}
