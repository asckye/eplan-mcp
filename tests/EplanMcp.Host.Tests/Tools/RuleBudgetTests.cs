using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class RuleBudgetTests
{
    [Theory]
    [InlineData(5000)]
    [InlineData(20000)]
    public void RuleCheck_Budget_TrimsRowsAfterEvaluationAndKeepsEvidence(int budget)
    {
        var rows = new JsonArray(Enumerable.Range(0, 200).Select(i => (JsonNode)new JsonObject
        {
            ["rule"] = "rule/motor",
            ["locator"] = i,
            ["status"] = "fail",
            ["evidence"] = new string('界', 400),
        }).ToArray());
        string before = rows.ToJsonString();
        JsonObject data = RuleTools.Page(new(), rows, JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["rule/motor"] = "source" }), 0, 200, budget, "fingerprint");
        Assert.True(data["truncated"]!.GetValue<bool>());
        Assert.InRange(data["rules"]!.AsArray().Count, 1, 199);
        Assert.Equal(before, rows.ToJsonString());
        Assert.Equal(new string('界', 400), data["rules"]![0]!["evidence"]!.GetValue<string>());
        Assert.True(ResultShaper.ResultTokens(data.ToJsonString(HostJson.Options)) + 100 <= budget);
        Assert.NotNull(data["nextCursor"]);
        Assert.Single(data["sources"]!.AsObject());
    }

    [Fact]
    public void RuleCheck_EnvelopeCannotFit_RequiresMoreBudget()
    {
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => RuleTools.Page(new(), [], JsonSerializer.SerializeToElement(new { }), 0, 50, 1, "fingerprint"));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        Assert.Equal("/max_tokens", error.Error.Metadata!["pointer"]);
    }
}
