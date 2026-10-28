using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class WorkflowCabinetTests
{
    private const string Fix = """{"workflow":"workflow/repair","inputs":{"target":{"object":true}}}""";
    [Fact]
    public async Task RuleCheck_UnboundProject_HasNoRulesOrBindingWrites()
    {
        await WithClient(async (client, unusedStore) =>
        {
            JsonElement data = ContextToolTests.Data(await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(cabinet: "default"), cancellationToken: Token));
            Assert.Empty(data.GetProperty("rules").EnumerateArray());
            Assert.False(File.Exists(BindingFile));
        });
    }
    private static Dictionary<string, object?> CheckRequest(bool fix = false, string cabinet = "a1") => new()
    {
        ["project"] = "Line1",
        ["cabinet"] = cabinet,
        ["scope"] = new { },
        ["fix_plan"] = fix
    };

    [Theory]
    [InlineData("pass", "pass")]
    [InlineData("fail", "fail")]
    [InlineData("unknown", "unknown")]
    [InlineData("not_applicable", "not_applicable")]
    public async Task RuleCheck_FourValues_AreSuccessfulQueriesWithBoundSources(string condition, string expected)
    {
        string review = await Seed(condition);
        await WithClient(async (client, unusedStore) =>
        {
            JsonElement data = ContextToolTests.Data(await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(), cancellationToken: Token));
            JsonElement result = Assert.Single(data.GetProperty("rules").EnumerateArray());
            Assert.Equal(expected, result.GetProperty("status").GetString());
            Assert.Equal("fact", result.GetProperty("basis").GetString());
            Assert.Equal("a1", data.GetProperty("cabinet").GetString());
            Assert.Equal(review, data.GetProperty("sources").GetProperty("acme.panels@1.0.0:rule/motor").GetProperty("reviewDigest").GetString());
            Assert.False(data.TryGetProperty("execution", out _));
            Assert.Equal(JsonValueKind.Null, data.GetProperty("planId").ValueKind);
            if (condition == "unknown")
            {
                Assert.Single(data.GetProperty("coverage").EnumerateArray());
                Assert.False(result.GetProperty("evidence")[0].GetProperty("complete").GetBoolean());
            }
            else
            { Assert.Equal("+A1/1", result.GetProperty("locator").GetProperty("name").GetString()); }
            AssertNoPlan();
        });
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task RuleCheck_ScopeAndExactRules_DoNotLeakAnotherCabinet()
    {
        await Seed("pass");
        await WithClient(async (client, unusedStore) =>
        {
            var request = CheckRequest();
            request["scope"] = new { pages = (string[])["+A10/2"] };
            Assert.Empty(ContextToolTests.Data(await client.CallToolAsync(RuleTools.CheckTool, request, cancellationToken: Token)).GetProperty("rules").EnumerateArray());
            request["rules"] = (string[])["acme.panels@2.0.0:rule/motor"];
            Assert.Equal(ErrorReason.ResourceNotFound, ContextToolTests.Error(await client.CallToolAsync(RuleTools.CheckTool, request, cancellationToken: Token)).GetProperty("reason").GetString());
        });
    }

    [Theory]
    [InlineData("no_review")]
    [InlineData("accepted")]
    public async Task RuleCheck_UnpromotedContent_IsRejected(string condition)
    {
        await Seed(condition);
        await WithClient(async (client, unusedStore) =>
        {
            Assert.Equal(ErrorReason.PackInvalid, ContextToolTests.Error(await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(), cancellationToken: Token)).GetProperty("reason").GetString());
            AssertNoPlan();
        });
    }

    [Fact]
    public async Task RuleCheck_Fix_PublishesOnlyPlanWithFailedPrestateAndFrozenWorkflowInputs()
    {
        await Seed("fail", Fix);
        await WithClient(async (client, store) =>
        {
            JsonElement data = ContextToolTests.Data(await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(fix: true), cancellationToken: Token));
            PlanRecord plan = (await store.ReadAsync(data.GetProperty("planId").GetString()!, Token))!;
            Assert.Equal("page.update", Assert.Single(plan.Definition.Steps).Op);
            Assert.Equal("+A1/1", plan.Definition.Steps[0].Args.GetProperty("pages")[0].GetProperty("name").GetString());
            Assert.Equal("fail", Assert.Single(plan.Definition.Engineering!.Acceptance.GetProperty("repairs").EnumerateArray()).GetProperty("status").GetString());
            JsonElement fix = Assert.Single(plan.Definition.Engineering.WorkflowRefAndInputs.GetProperty("inputs").GetProperty("fixes").EnumerateArray());
            Assert.Equal("acme.panels@1.0.0:workflow/repair", fix.GetProperty("workflow").GetString());
            Assert.Equal("+A1/1", fix.GetProperty("inputs").GetProperty("target").GetString());
            Assert.Equal(PlanCanonical.Hash(plan.Definition), plan.PlanHash);
        });
        Assert.Empty(_bridge.Writes);
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation.EndsWith(".execute", StringComparison.Ordinal) || c.Operation == OperationNames.ProjectBackup);
    }

    [Fact]
    public async Task RuleCheck_Unknown_RefusesFixPlanWithoutPublishing()
    {
        await Seed("unknown", Fix);
        await WithClient(async (client, unusedStore) =>
        {
            JsonElement error = ContextToolTests.Error(await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(fix: true), cancellationToken: Token));
            Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
            Assert.False(error.TryGetProperty("execution", out _));
            AssertNoPlan();
        });
        Assert.Empty(_bridge.Writes);
    }

    [Fact]
    public async Task RuleCheck_ConflictingFixes_RefusePlanBeforeCompilation()
    {
        await Seed("fail", """{"workflow":"workflow/repair","inputs":{"target":{"literal":"+A1/1"}}}""");
        WholeCabinet();
        await WithClient(async (client, unusedStore) =>
        {
            Assert.Equal(ErrorReason.ConfigConflict, ContextToolTests.Error(await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(fix: true, cabinet: "default"), cancellationToken: Token)).GetProperty("reason").GetString());
            AssertNoPlan();
        });
        Assert.DoesNotContain(_bridge.Calls, c => c.Operation == OperationNames.PageUpdatePlan);
    }

    [Fact]
    public async Task RuleCheck_Cursor_BindsRevisionAndReturnsFullRows()
    {
        await Seed("fail");
        WholeCabinet();
        await WithClient(async (client, unusedStore) =>
        {
            var request = CheckRequest(cabinet: "default");
            request["limit"] = 1;
            JsonElement first = ContextToolTests.Data(await client.CallToolAsync(RuleTools.CheckTool, request, cancellationToken: Token));
            Assert.Equal(2, first.GetProperty("total").GetInt32());
            request["cursor"] = first.GetProperty("nextCursor").GetString();
            JsonElement second = ContextToolTests.Data(await client.CallToolAsync(RuleTools.CheckTool, request, cancellationToken: Token));
            Assert.NotEqual(first.GetProperty("rules")[0].GetProperty("objectHash").GetString(), second.GetProperty("rules")[0].GetProperty("objectHash").GetString());
            Assert.False(second.GetProperty("truncated").GetBoolean());
            ProjectBinding binding = PackJson.ReadBinding(File.ReadAllBytes(BindingFile));
            SaveBinding(binding with { Revision = binding.Revision + 1 });
            Assert.Equal(ErrorReason.InvalidArgument, ContextToolTests.Error(await client.CallToolAsync(RuleTools.CheckTool, request, cancellationToken: Token)).GetProperty("reason").GetString());
        });
    }

    [Fact]
    public async Task RuleCheck_RuleChangesDuringCompilation_RefusesPublication()
    {
        await Seed("fail", Fix);
        Func<object?, object> original = _bridge.Handlers[OperationNames.PageUpdatePlan];
        _bridge.Handlers[OperationNames.PageUpdatePlan] = args =>
        {
            ProjectBinding binding = PackJson.ReadBinding(File.ReadAllBytes(BindingFile));
            SaveBinding(binding with { Revision = 8 });
            return original(args);
        };
        await WithClient(async (client, unusedStore) =>
        {
            Assert.True((await client.CallToolAsync(RuleTools.CheckTool, CheckRequest(fix: true), cancellationToken: Token)).IsError);
            AssertNoPlan();
        });
    }

    private void WholeCabinet()
    {
        ProjectBinding binding = PackJson.ReadBinding(File.ReadAllBytes(BindingFile));
        SaveBinding(binding with { Cabinets = [binding.Cabinets[0] with { Id = "default", Structure = new JsonObject { ["all"] = true } }] });
    }
}
