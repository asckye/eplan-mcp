using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanStepSemanticsTests
{
    [Theory]
    [InlineData("page.create", "exists", "pageType")]
    [InlineData("page.update", "value", "values/11011/en_US")]
    [InlineData("device.update", "value", "name")]
    [InlineData("macro.insert", "exists", "page.exists")]
    [InlineData("page.delete", "deleted", "exists")]
    [InlineData("page.renumber", "numbering", "page.name")]
    [InlineData("device.renumber", "numbering", "function.name")]
    public void Build_EachOperation_HasStableExpectedSourceAndCoverage(string op, string tag, string property)
    {
        PlanStep step = Compile(op);
        Assert.True(step.Effective);
        Assert.Contains(step.Expected, e => e.GetProperty("tag").GetString() == tag);
        Assert.All(step.Expected, e =>
        {
            Assert.Equal(op + ".plan", e.GetProperty("source").GetProperty("operation").GetString());
            Assert.Equal(1, e.GetProperty("source").GetProperty("step").GetInt32());
            Assert.NotEmpty(e.GetProperty("coveredProperties").EnumerateArray());
        });
        Assert.Contains(property, step.Coverage!.Value.GetProperty("properties").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("graphics", step.Coverage.Value.GetProperty("unknown").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(3, step.Coverage.Value.GetProperty("skippedConnections").GetInt32());
        Assert.Equal(["+A1/1", "+A1/2", "+A10/1"], step.Coverage.Value.GetProperty("baselinePages").EnumerateArray().Select(p => p.GetString()));
        if (op == "page.create")
        { Assert.Contains("+A1/3", step.Coverage.Value.GetProperty("pages").EnumerateArray().Select(p => p.GetString())); }
    }

    [Theory]
    [InlineData("device.update")]
    [InlineData("macro.insert")]
    [InlineData("page.delete")]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public void Build_RuntimeFieldsAndUnknownDetails_NeverEnterDefinitionOrPreview(string op)
    {
        WritePlan plan = PlanStepCompilationData.Plan(op);
        plan.Fingerprint = "session-secret";
        plan.Details ??= [];
        plan.Details["ToStringIdentifier"] = "runtime-secret";
        plan.Details["DatabaseIdentifier"] = "database-secret";
        plan.Details["fingerprint"] = "nested-secret";
        plan.Details["objects"] = "opaque-runtime-secret";
        PlanStep step = Compile(op, plan);
        var definition = PlanTestData.Definition() with { Steps = [step] };
        string content = PlanCanonical.Serialize(definition) + PlanDiffPreview.For([step]);
        foreach (string forbidden in new[] { "session-secret", "runtime-secret", "database-secret", "nested-secret", "opaque-runtime-secret", "Fingerprint", "ToStringIdentifier", "DatabaseIdentifier" })
        { Assert.DoesNotContain(forbidden, content, StringComparison.OrdinalIgnoreCase); }
        Assert.Equal("session-secret", plan.Fingerprint);
        Assert.Equal(plan.Changes.Select(c => c.Name), step.PlannedChanges.Select(c => c.Name));
        plan.Changes[0].Name = "mutated";
        plan.Details.Clear();
        Assert.DoesNotContain("mutated", PlanCanonical.Serialize(definition), StringComparison.Ordinal);
        Assert.NotEmpty(step.Details);
        Assert.Equal(PlanCanonical.Serialize(definition), PlanCanonical.Serialize(PlanCanonical.ReadDefinition(PlanCanonical.Serialize(definition))));
    }

    [Theory]
    [InlineData("page.update")]
    [InlineData("device.renumber")]
    public void Build_NoChanges_IsIneffectiveAndKeepsRequestedPostcondition(string op)
    {
        WritePlan plan = PlanStepCompilationData.Plan(op);
        plan.Changes.Clear();
        PlanStep step = Compile(op, plan);
        Assert.False(step.Effective);
        Assert.Empty(step.PlannedChanges);
        Assert.NotEmpty(step.Expected);
        Assert.Null(step.Prediction);
        Assert.Contains("No changes.", PlanDiffPreview.For([step]), StringComparison.Ordinal);
        Assert.Equal(PlanExecution.NotStarted, PlanResult.Aggregate([new(step.Index, op, step.Effective, ExecutionState.NotStarted)]).Execution);
    }

    [Fact]
    public void Build_CreateAndClearDescription_StoreExactLanguageTypeAndNull()
    {
        JsonElement created = Assert.Single(Compile("page.create").Expected);
        Assert.Equal("+A1/3", created.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal("Circuit", created.GetProperty("value").GetProperty("pageType").GetString());
        Assert.Equal("en_US", created.GetProperty("value").GetProperty("language").GetString());
        Assert.Equal("Power", created.GetProperty("value").GetProperty("description").GetString());
        PlanArgumentStep input = PlanStepCompilationData.Input("page.update", """{"pages":[{"name":"+a1/1","description":""}],"language":"en_US"}""");
        JsonElement cleared = Assert.Single(Compile(input).Expected);
        Assert.Equal("+A1/1", cleared.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("value").GetProperty("description").ValueKind);
        Assert.Equal("page-+A1/1", cleared.GetProperty("source").GetProperty("hash").GetString());
    }

    [Fact]
    public void Build_DeviceRename_UsesResolvedFullTagForEveryFunction()
    {
        PlanStep step = Compile("device.update");
        Assert.Equal([11L, 12], step.Expected.Select(e => e.GetProperty("target").GetProperty("oid").GetInt64()));
        Assert.All(step.Expected, e => Assert.Equal("+A1-K9", e.GetProperty("value").GetProperty("name").GetString()));
    }

    [Fact]
    public void Build_Delete_SeparatesDeletedObjectsFromSurvivingOrphans()
    {
        PlanStep step = Compile("page.delete");
        Assert.Equal([1L, 11], step.Expected.Where(e => e.GetProperty("tag").GetString() == "deleted")
            .Select(e => e.GetProperty("target").GetProperty("oid").GetInt64()));
        JsonElement orphan = Assert.Single(step.Expected, e => e.GetProperty("tag").GetString() == "orphaned");
        Assert.False(orphan.GetProperty("value").GetProperty("hasMain").GetBoolean());
        Assert.Equal(OperationNames.PlanFunctions, orphan.GetProperty("source").GetProperty("evidence").GetString());
        Assert.Equal(12, Assert.Single(orphan.GetProperty("value").GetProperty("remaining").EnumerateArray()).GetProperty("oid").GetInt64());
        Assert.Contains("main function 'present' -> 'absent'", PlanDiffPreview.For([step]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("page.renumber")]
    [InlineData("device.renumber")]
    public void Build_Numbering_PreservesConstraintAndPromisesNoExactName(string op)
    {
        PlanStep step = Compile(op);
        JsonElement constraint = Assert.Single(step.Expected).GetProperty("value");
        Assert.False(constraint.GetProperty("exactNames").GetBoolean());
        Assert.Equal(op == "device.renumber", constraint.GetProperty("mayRemainUnnumbered").GetBoolean());
        Assert.Equal(10, constraint.GetProperty("args").GetProperty("start").GetInt32());
        Assert.Equal(2, constraint.GetProperty("args").GetProperty("step").GetInt32());
        Assert.Contains("numbering.exactNames", step.Coverage!.Value.GetProperty("unknown").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("start: 10", PlanDiffPreview.For([step]), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MacroSample_IsKeptAsDetailsWithExplicitVerificationGaps()
    {
        PlanStep step = Compile("macro.insert");
        Assert.Equal("8", step.Details["deviceCount"]);
        Assert.Single(step.Details["devices"].Split('\n'));
        string[] unknown = [.. step.Coverage!.Value.GetProperty("unknown").EnumerateArray().Select(p => p.GetString()!)];
        Assert.Contains("macro.functions", unknown);
        Assert.Contains("macro.graphics", unknown);
        Assert.Contains("macro.placeholders", unknown);
        Assert.Contains("macro.fileHash", unknown);
    }

    [Fact]
    public void Build_ChangedRuntimeFingerprint_LeavesDefinitionHashAndPreviewIdentical()
    {
        WritePlan first = PlanStepCompilationData.Plan("device.update"), second = PlanStepCompilationData.Plan("device.update");
        second.Fingerprint = "another-session";
        second.Details!["sessionFingerprint"] = "another-session";
        second.Details["objectIds"] = "session-only-identifiers";
        PlanStep a = Compile("device.update", first), b = Compile("device.update", second);
        Assert.Equal(PlanCanonical.Hash(PlanTestData.Definition() with { Steps = [a] }), PlanCanonical.Hash(PlanTestData.Definition() with { Steps = [b] }));
        Assert.Equal(PlanDiffPreview.For([a]), PlanDiffPreview.For([b]));
    }

    [Fact]
    public void Preview_GroupsByStepThenPage_UsesWriteChangeTextAndEscapesWithoutTruncation()
    {
        PlanStep first = Compile("page.create") with { Index = 2 };
        string text = "Line\n\t\u202e" + new string('x', 1000);
        PlanStep second = Compile("page.update") with
        { Index = 1, PlannedChanges = [new("update", "page", "+A1/1", "description (en_US)", "old", text)] };
        string preview = PlanDiffPreview.For([first, second]);
        Assert.StartsWith("Step 1: page.update [write]\n  Page +A1/1\n", preview, StringComparison.Ordinal);
        Assert.Contains("update page +A1/1: description (en_US) 'old' -> 'Line\\u000a\\u0009\\u202e" + new string('x', 1000), preview, StringComparison.Ordinal);
        Assert.Contains("Step 2: page.create [write]\n  Page +A1/3\n    create page +A1/3 (Circuit, \"Power\")", preview, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', preview);
    }

    private static PlanStep Compile(string op, WritePlan? plan = null) => Compile(PlanStepCompilationData.Input(op), plan);

    private static PlanStep Compile(PlanArgumentStep input, WritePlan? plan = null)
    {
        PlanBaseline baseline = PlanScopeTestData.Baseline();
        baseline = baseline with { Snapshot = baseline.Snapshot with { SkippedConnections = 3 } };
        PlanScope whole = PlanScopeRules.Whole(baseline.Snapshot);
        SnapshotObject[] objects = [.. baseline.Snapshot.Pages.SelectMany(p => p.Objects)];
        PlanScope modify = input.Op switch
        {
            "page.create" => PlanScopeRules.Pages(["+A1/3"]),
            "page.update" or "macro.insert" => PlanScopeRules.Pages(["+A1/1"]),
            "device.update" => new(["+A1/1", "+A1/2"], [], [.. objects.Where(o => o.Name == "+A1-K1").Select(o => PlanScopeRules.Locator(baseline.Snapshot.Project.Path, o))]),
            _ => whole,
        };
        var impact = new PlanImpact(whole, modify, [], baseline.NamesHash,
            input.Op == "page.delete" ? [new("+A1-K1", [PlanScopeRules.Locator(baseline.Snapshot.Project.Path, objects.Single(o => o.Oid == 12))])] : []);
        return PlanStepSemantics.Build(baseline, input, impact, plan ?? PlanStepCompilationData.Plan(input.Op));
    }
}

internal static class PlanStepCompilationData
{
    public static PlanArgumentStep Input(string op, string? args = null)
    {
        args ??= op switch
        {
            "page.create" => """{"pages":[{"name":"+A1/3","description":"Power"}],"language":"en_US"}""",
            "page.update" => """{"pages":[{"name":"+A1/1","description":"New"}],"language":"en_US"}""",
            "device.update" => """{"tag":"-K1","new_tag":"-K9"}""",
            "macro.insert" => """{"page":"+A1/1","macro":"Motor.ema","x":40.5,"y":280.25}""",
            "page.delete" => """{"pages":["+A1/1"]}""",
            "page.renumber" => """{"pages":["+A1/1"],"start":10,"step":2,"keep_text":true,"subpages":"consecutive"}""",
            "device.renumber" => """{"pages":["+A1/1"],"only_unnumbered":false,"scheme":" IEC ","start":10,"step":2}""",
            _ => throw new ArgumentException("Unknown test operation.", nameof(op)),
        };
        PlanArgumentCheck check = PlanArguments.Check(PlanTestData.Json("{\"project\":\"Line1\",\"steps\":[{\"op\":\"" + op + "\",\"args\":" + args + "}]}"), "en_US", "IEC");
        Assert.Empty(check.Violations);
        return Assert.Single(check.Steps);
    }

    public static WritePlan Plan(string op)
    {
        PlannedChange change = op switch
        {
            "page.create" => new() { Action = "create", Kind = "page", Name = "+A1/3", NewValue = "Circuit, \"Power\"" },
            "page.update" => new() { Action = "update", Kind = "page", Name = "+A1/1", Property = "description (en_US)", OldValue = "Old", NewValue = "New" },
            "device.update" => new() { Action = "update", Kind = "device", Name = "+A1-K1", Property = "tag", OldValue = "+A1-K1", NewValue = "+A1-K9" },
            "macro.insert" => new() { Action = "create", Kind = "macro", Name = "+A1/1", NewValue = "Motor.ema at (40.5, 280.25), numbering keep, 8 devices" },
            "page.delete" => new() { Action = "delete", Kind = "page", Name = "+A1/1", OldValue = "Circuit, 1 function" },
            "page.renumber" => new() { Action = "update", Kind = "page", Name = "+A1/1", Property = "page number" },
            "device.renumber" => new() { Action = "update", Kind = "devices", Name = "Line1", Property = "device tags", OldValue = "all devices on 1 page" },
            _ => throw new ArgumentException("Unknown test operation.", nameof(op)),
        };
        return new()
        {
            Project = "Line1",
            ProjectPath = Bridge.PlanFunctionsBridge.Project,
            Fingerprint = "session-secret",
            Changes = [change],
            Details = new() { ["functions"] = "2", ["pages"] = "+A1/1", ["deviceCount"] = "8", ["devices"] = "-K?\t-K?\tCoil", ["objectCount"] = "10", ["start"] = "10", ["step"] = "2", ["orphanedCount"] = "1" },
        };
    }
}
