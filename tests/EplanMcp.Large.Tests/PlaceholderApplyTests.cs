using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Large.Tests;

/// <summary>
/// eplan_placeholder_apply on the write copy (README.md), never on the read tests' fixture
/// (docs/tools/eplan_placeholder_apply.md, section 7). The copy is the sample project, so =TM1+A1&amp;EFS1/1 has the
/// placeholder spike S29 found (api-notes A33): no name, value sets V and mA, variables T1 and T2. S29 applied value sets
/// only inside a rolled-back SafetyPoint; this test verifies the rest live: the value set stays applied after the commit,
/// inside the UndoStep and QuietModeStep, and on a placeholder the project already had. Each run ends with V applied.
/// </summary>
[Trait("Size", "Large")]
public sealed class PlaceholderApplyTests(LiveEplan eplan, WriteHost host) : IClassFixture<WriteHost>
{
    private const string Page = "=TM1+A1&EFS1/1";

    [Fact]
    public async Task ApplyingMaThenV_ChangesTheCurrentValuesToEachValueSetsValues()
    {
        string project = await eplan.WriteProjectAsync();
        Dictionary<string, object?> mA = new() { ["project"] = project, ["page"] = Page, ["placeholder"] = string.Empty, ["value_set"] = "mA" };

        JsonElement dryRun = await eplan.CallAsync("eplan_placeholder_apply", new(mA) { ["dry_run"] = true });
        JsonElement appliedMa = await eplan.CallAsync("eplan_placeholder_apply", mA);
        string[] afterMa = await CurrentTextsAsync(project);
        JsonElement appliedV = await eplan.CallAsync("eplan_placeholder_apply", new(mA) { ["value_set"] = "V" });
        string[] afterV = await CurrentTextsAsync(project);

        Assert.Equal(("not_started", JsonValueKind.Null), (dryRun.GetProperty("execution").GetString(), dryRun.GetProperty("undo").ValueKind));
        Assert.Equal(["T1", "T2"], dryRun.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("variable").GetString()));
        Assert.Equal(("committed", "AI: eplan_placeholder_apply"), (appliedMa.GetProperty("execution").GetString(), appliedMa.GetProperty("undo").GetString()));
        Assert.Equal("committed", appliedV.GetProperty("execution").GetString());

        // S29: after ApplyRecord the current values are the value set's texts; here read again by eplan_placeholder_list.
        Assert.Equal(Texts(appliedMa.GetProperty("values")).Order(StringComparer.Ordinal), afterMa.Order(StringComparer.Ordinal));
        Assert.Equal(Texts(appliedV.GetProperty("values")).Order(StringComparer.Ordinal), afterV.Order(StringComparer.Ordinal));
        if (!Texts(appliedMa.GetProperty("values")).Order(StringComparer.Ordinal).SequenceEqual(Texts(appliedV.GetProperty("values")).Order(StringComparer.Ordinal)))
        {
            Assert.True(appliedV.GetProperty("currentValuesChanged").GetBoolean());
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"dry run: {dryRun.GetProperty("currentValuesBefore")}; mA: {appliedMa.GetProperty("values")} -> {JsonSerializer.Serialize(afterMa)} (changed: {appliedMa.GetProperty("currentValuesChanged")}); " +
            $"V: {appliedV.GetProperty("values")} -> {JsonSerializer.Serialize(afterV)}. To check Ctrl+Z, apply mA once more with only the write copy open, press Ctrl+Z, and list the placeholder: its current values must be V's again.");
    }

    [Fact]
    public async Task AValueSetOrANameThePageDoesNotHave_ChangesNothing()
    {
        string project = await eplan.WriteProjectAsync();
        Dictionary<string, object?> args = new() { ["project"] = project, ["page"] = Page, ["value_set"] = "X" };

        JsonElement noValueSet = await eplan.ErrorAsync("eplan_placeholder_apply", args);
        JsonElement noName = await eplan.ErrorAsync("eplan_placeholder_apply", new(args) { ["value_set"] = "mA", ["placeholder"] = "nope" });

        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (noValueSet.GetProperty("reason").GetString(), noValueSet.GetProperty("execution").GetString()));
        Assert.Equal("(no name): V, mA", noValueSet.GetProperty("metadata").GetProperty("candidates").GetString());
        Assert.Equal((ErrorReason.InvalidArgument, ExecutionState.NotStarted), (noName.GetProperty("reason").GetString(), noName.GetProperty("execution").GetString()));
    }

    [Fact]
    public async Task TheCopyOutsideScratch_IsPolicyDenied()
    {
        JsonElement error = await host.ErrorAsync("eplan_placeholder_apply", new() { ["project"] = host.Outside, ["page"] = Page, ["value_set"] = "mA" });

        Assert.Equal((ErrorReason.PolicyDenied, ExecutionState.NotStarted), (error.GetProperty("reason").GetString(), error.GetProperty("execution").GetString()));
    }

    private async Task<string[]> CurrentTextsAsync(string project)
    {
        JsonElement listed = await eplan.CallAsync("eplan_placeholder_list", new() { ["project"] = project, ["page"] = Page, ["response_format"] = "detailed" });
        return Texts(Assert.Single(listed.GetProperty("placeholders").EnumerateArray()).GetProperty("currentValues"));
    }

    private static string[] Texts(JsonElement values) => [.. values.EnumerateArray().Select(v => v.GetProperty("text").GetString() ?? string.Empty)];
}
