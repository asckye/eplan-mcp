using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
public sealed class PageReviewRulePackTests
{
    [Fact]
    public void OutputSchema_DeclaresRuleCodesReferencesSummariesAndPluginMetadata()
    {
        var tool = new Tool { Name = PageReviewTools.Tool, InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }) };
        PageReviewTools.Configure(tool, new JsonObject { ["properties"] = new JsonObject() });
        JsonElement properties = tool.OutputSchema!.Value.GetProperty("properties");
        JsonElement issue = properties.GetProperty("issues").GetProperty("items").GetProperty("properties");
        string pattern = issue.GetProperty("code").GetProperty("anyOf")[1].GetProperty("pattern").GetString()!;
        Assert.Matches(pattern, "rule:review.fixture/designation-letters");
        Assert.Matches(pattern, "rule_unknown:review.fixture/wire-colour");
        Assert.True(issue.TryGetProperty("reference", out _));
        Assert.Equal("array", properties.GetProperty("rulePacks").GetProperty("type").GetString());
        Assert.Equal("array", properties.GetProperty("metadata").GetProperty("properties").GetProperty("plugins").GetProperty("type").GetString());
    }

    [Fact]
    public async Task FailedRules_ReportSeverityReferenceLocalizedMessageAndLocatedFix()
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(tag: "-S1", colour: "other");

        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync([PageReviewRulePackFixture.Plugin]));

        JsonElement[] issues = result.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Equal(2, issues.Length);
        Assert.Equal(["rule:review.fixture/designation-letters", "rule:review.fixture/wire-colour"], issues.Select(i => i.GetProperty("code").GetString()));
        Assert.Equal(["error", "warning"], issues.Select(i => i.GetProperty("severity").GetString()));
        Assert.Equal("标识字母不在夹具允许值中", issues[0].GetProperty("message").GetString());
        Assert.Equal("Fixture rule failed", issues[1].GetProperty("message").GetString());
        Assert.Equal("Fixture standard:2026", issues[0].GetProperty("reference").GetProperty("standard").GetString());
        Assert.Equal("1.2", issues[1].GetProperty("reference").GetProperty("clause").GetString());
        Assert.Equal("[8,0]", issues[0].GetProperty("position").GetRawText());
        foreach (JsonElement issue in issues)
        {
            _ = Assert.Single(issue.GetProperty("objects").EnumerateArray());
            string code = issue.GetProperty("code").GetString()!;
            Assert.Equal(code.EndsWith("wire-colour", StringComparison.Ordinal) ? "eplan_connection_create" : "eplan_device_update", issue.GetProperty("fix").GetProperty("tool").GetString());
        }
        JsonElement pack = Assert.Single(result.GetProperty("rulePacks").EnumerateArray());
        Assert.Equal("draft", pack.GetProperty("status").GetString());
        Assert.Equal(2, pack.GetProperty("rules").GetInt32());
        Assert.Equal(2, pack.GetProperty("failed").GetInt32());
        Assert.Equal(0, pack.GetProperty("passed").GetInt32());
        Assert.Equal(0, pack.GetProperty("unknown").GetInt32());
        Assert.Equal(0, pack.GetProperty("notApplicable").GetInt32());
        Assert.Equal("Fixture standard:2026", Assert.Single(pack.GetProperty("standards").EnumerateArray()).GetString());
        Assert.Equal("review.fixture@1.0.0", Assert.Single(result.GetProperty("metadata").GetProperty("plugins").EnumerateArray()).GetString());
        Assert.False(result.TryGetProperty("actual", out _));
        Assert.Empty(fixture.Bridge.Writes);
    }

    [Theory]
    [InlineData(true, 2, 0)]
    [InlineData(false, 1, 1)]
    public async Task PassingAndOutOfScopeObjects_ProduceNoIssues(bool main, int passed, int notApplicable)
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(main: main);
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync([PageReviewRulePackFixture.Plugin]));
        Assert.Empty(result.GetProperty("issues").EnumerateArray());
        Assert.True(result.GetProperty("summary").GetProperty("clean").GetBoolean());
        JsonElement pack = Assert.Single(result.GetProperty("rulePacks").EnumerateArray());
        Assert.Equal(passed, pack.GetProperty("passed").GetInt32());
        Assert.Equal(notApplicable, pack.GetProperty("notApplicable").GetInt32());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public async Task MissingFacts_AggregateOneUnknownPerRuleWithAtMostTwentyLocators(string? tag, string? colour)
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(25, tag, colour);
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync([PageReviewRulePackFixture.Plugin]));
        JsonElement[] issues = result.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Equal(["rule_unknown:review.fixture/designation-letters", "rule_unknown:review.fixture/wire-colour"], issues.Select(i => i.GetProperty("code").GetString()));
        Assert.Equal(["warning", "info"], issues.Select(i => i.GetProperty("severity").GetString()));
        Assert.All(issues, issue =>
        {
            Assert.Equal(20, issue.GetProperty("objects").GetArrayLength());
            Assert.Contains("25 objects", issue.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.True(issue.TryGetProperty("reference", out _));
        });
        Assert.Contains("function.letters", issues[0].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("connection.colour", issues[1].GetProperty("message").GetString(), StringComparison.Ordinal);
        JsonElement pack = Assert.Single(result.GetProperty("rulePacks").EnumerateArray());
        Assert.Equal(50, pack.GetProperty("unknown").GetInt32());
        Assert.Equal(0, pack.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task ChecksWithoutRules_DoNotResolveOrEvaluatePlugins()
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.ProjectPath = Path.Combine(fixture.Locations.Root, "production", "Line1");
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(tag: "-S1", colour: "other");
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync([PageReviewRulePackFixture.Plugin], ["off_grid"]));
        Assert.Empty(result.GetProperty("issues").EnumerateArray());
        Assert.False(result.TryGetProperty("rulePacks", out _));
        Assert.False(result.TryGetProperty("metadata", out _));
        Assert.Equal(OperationNames.PageReview, Assert.Single(fixture.Bridge.Calls).Operation);
    }

    [Theory]
    [InlineData("missing.fixture")]
    [InlineData("empty.fixture")]
    public async Task ExplicitUnavailableOrRulelessPlugin_IsRejectedWithAvailableIds(string id)
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.WritePack("empty.fixture", rules: false);
        JsonElement error = WriteToolHarness.Error(await fixture.ReviewAsync([id]));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("review.fixture", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Bridge.Calls, call => call.Operation == OperationNames.PageReview);
    }

    [Fact]
    public async Task ExplicitPluginWithOnlyLegacyRules_IsRejectedAsHavingNoSupportedRules()
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.WritePack("legacy.fixture");
        fixture.MakeRulesLegacy("legacy.fixture");

        JsonElement error = WriteToolHarness.Error(await fixture.ReviewAsync(["legacy.fixture"]));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("contains no rules: legacy.fixture", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Installed plugins with rules: review.fixture", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Bridge.Calls, call => call.Operation == OperationNames.PageReview);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableOrMissingRuleDocuments_WarnWithoutRefusalOrViolation(bool missing)
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.MakeRuleUnavailable("designation-letters", missing);
        fixture.MakeRuleUnavailable("wire-colour", missing);
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(tag: "-S1", colour: "other");

        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync([PageReviewRulePackFixture.Plugin]));

        Assert.Empty(result.GetProperty("issues").EnumerateArray());
        Assert.True(result.GetProperty("summary").GetProperty("clean").GetBoolean());
        Assert.False(result.TryGetProperty("rulePacks", out _));
        Assert.False(result.GetProperty("metadata").TryGetProperty("plugins", out _));
        Assert.NotEmpty(result.GetProperty("metadata").GetProperty("warnings").EnumerateArray());
        Assert.Contains(fixture.Bridge.Calls, call => call.Operation == OperationNames.PageReview);
        Assert.Empty(fixture.Bridge.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutsideScratch_RequiresEngineerSignoff(bool isSigned)
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack(isSigned: isSigned);
        fixture.ProjectPath = Path.Combine(fixture.Locations.Root, "production", "Line1");
        CallToolResult result = await fixture.ReviewAsync([PageReviewRulePackFixture.Plugin]);
        if (isSigned)
        {
            Assert.Equal("signed", Assert.Single(WriteToolHarness.Structured(result).GetProperty("rulePacks").EnumerateArray()).GetProperty("status").GetString());
        }
        else
        {
            JsonElement error = WriteToolHarness.Error(result);
            Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
            Assert.Contains("sign off", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Bridge.Calls, call => call.Operation == OperationNames.PageReview);
        }
    }

    [Fact]
    public async Task OmittedSelection_UsesEnabledPluginAndStopsUsingItAfterDisable()
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack();
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(tag: "-S1", colour: "other");
        JsonElement baseline = WriteToolHarness.Structured(await fixture.ReviewAsync(useLibrary: false));
        Assert.Equal(baseline.GetRawText(), WriteToolHarness.Structured(await fixture.ReviewAsync()).GetRawText());
        await fixture.EnableAsync(PageReviewRulePackFixture.Plugin);
        JsonElement enabled = WriteToolHarness.Structured(await fixture.ReviewAsync());
        Assert.Equal(2, enabled.GetProperty("issues").GetArrayLength());
        Assert.Equal("review.fixture@1.0.0", Assert.Single(enabled.GetProperty("metadata").GetProperty("plugins").EnumerateArray()).GetString());
        await fixture.EnableAsync();
        Assert.Equal(baseline.GetRawText(), WriteToolHarness.Structured(await fixture.ReviewAsync()).GetRawText());
    }

    [Fact]
    public async Task DetailedReviewWithoutRules_KeepsExistingReadbackWhenInternalFactsAreAdded()
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.Evidence["issues"]!.AsArray().Add(new JsonObject
        {
            ["code"] = "overlap",
            ["severity"] = "warning",
            ["page"] = PageReviewRulePackFixture.Page,
            ["position"] = new JsonArray(8, 0),
            ["objects"] = new JsonArray(PageReviewRulePackFixture.Locator(1), PageReviewRulePackFixture.Locator(101)),
            ["message"] = "Fixture overlap",
            ["fix"] = new JsonObject { ["tool"] = "eplan_placement_update", ["args"] = new JsonObject() },
        });
        JsonObject withFacts = fixture.Evidence.DeepClone().AsObject();
        JsonObject oldDevice = fixture.Evidence["actual"]!["devices"]![0]!.AsObject();
        oldDevice.Remove("category");
        oldDevice.Remove("main");
        JsonObject oldConnection = fixture.Evidence["actual"]!["connections"]![0]!.AsObject();
        foreach (string field in new[] { "crossSection", "potentialName", "potentialType" })
        { oldConnection.Remove(field); }
        JsonElement baseline = WriteToolHarness.Structured(await fixture.ReviewAsync(checks: ["off_grid"], responseFormat: "detailed"));

        fixture.Evidence = withFacts;
        JsonElement result = WriteToolHarness.Structured(await fixture.ReviewAsync(checks: ["off_grid"], responseFormat: "detailed"));

        Assert.Equal(2, result.GetProperty("issues")[0].GetProperty("readback").GetArrayLength());
        Assert.Equal(baseline.GetRawText(), result.GetRawText());
    }

    [Fact]
    public async Task OversizedPluginMetadata_ReturnsBudgetGuidanceInsteadOfNonprogressingPage()
    {
        using var fixture = new PageReviewRulePackFixture();
        string[] standards = Enumerable.Range(0, 64).Select(i => i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 126)).ToArray();
        string[] ids = Enumerable.Range(0, 10).Select(i => "review.budget-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        foreach (string id in ids)
        { fixture.WritePack(id, isSigned: true, standards: standards); }
        fixture.Evidence = PageReviewRulePackFixture.CreateEvidence(tag: "-S1", colour: "other");

        JsonElement error = WriteToolHarness.Error(await fixture.ReviewAsync(ids, limit: 1));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("budget", error.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Bridge.Writes);
    }

    [Fact]
    public async Task PaginationFingerprint_TracksRulePackArgumentsEvenWhenRulesAreExcluded()
    {
        using var fixture = new PageReviewRulePackFixture();
        fixture.WritePack("first.fixture");
        fixture.WritePack("second.fixture");
        for (int i = 0; i < 3; i++)
        {
            fixture.Evidence["issues"]!.AsArray().Add(new JsonObject
            {
                ["code"] = "off_grid",
                ["severity"] = "warning",
                ["page"] = PageReviewRulePackFixture.Page,
                ["position"] = new JsonArray(i, 1),
                ["objects"] = new JsonArray(),
                ["message"] = "Fixture grid issue",
                ["fix"] = new JsonObject { ["tool"] = "eplan_placement_update", ["args"] = new JsonObject() },
            });
        }
        JsonElement first = WriteToolHarness.Structured(await fixture.ReviewAsync(["first.fixture"], ["off_grid"], limit: 1));
        string cursor = first.GetProperty("cursor").GetString()!;
        Assert.Single(WriteToolHarness.Structured(await fixture.ReviewAsync(["first.fixture"], ["off_grid"], limit: 1, cursor: cursor)).GetProperty("issues").EnumerateArray());
        JsonElement error = WriteToolHarness.Error(await fixture.ReviewAsync(["second.fixture"], ["off_grid"], limit: 1, cursor: cursor));
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("cursor", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
