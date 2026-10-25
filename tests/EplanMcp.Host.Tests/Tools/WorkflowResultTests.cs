using System.Text.Json;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class WorkflowResultTests
{
    [Theory]
    [InlineData(5000)]
    [InlineData(20000)]
    [InlineData(100000)]
    public void LargeResult_FitsBothJsonCopiesAndLink_WithoutChangingTheDefinition(int budget)
    {
        PlanDefinition definition = PlanTestData.Definition();
        PlanStep step = definition.Steps[0] with
        {
            PlannedChanges = [.. Enumerable.Range(0, 100).Select(i => new PlanChange("update", "page", "+A/1", "description", null, new string('界', 500)))],
        };
        definition = definition with { Steps = [step] };
        string before = PlanCanonical.Serialize(definition);
        CallToolResult result = WorkflowResults.Success(new(1, PlanCanonical.Hash(definition), definition, new()), budget);
        JsonElement data = result.StructuredContent!.Value;
        Assert.False(result.IsError == true);
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Equal(100, data.GetProperty("counts").GetProperty("changes").GetInt32());
        Assert.NotNull(data.GetProperty("hint").GetString());
        Assert.Equal("Compiled a plan with 1 steps; no project data was changed.", ((TextContentBlock)result.Content[0]).Text);
        Assert.True(JsonElement.DeepEquals(data, JsonSerializer.Deserialize<JsonElement>(((TextContentBlock)result.Content[1]).Text)));
        ResourceLinkBlock link = Assert.IsType<ResourceLinkBlock>(result.Content[2]);
        Assert.Equal(data.GetProperty("resource").GetString(), link.Uri);
        int tokens = ResultShaper.ResultTokens(data.GetRawText()) + ResultShaper.EstimateTokens(((TextContentBlock)result.Content[0]).Text)
            + ResultShaper.EstimateTokens(JsonSerializer.Serialize(link, HostJson.Options));
        Assert.InRange(tokens, 1, Math.Min(budget, 20000));
        Assert.Equal(before, PlanCanonical.Serialize(definition));
        Assert.False(data.TryGetProperty("execution", out _));
        Assert.False(data.TryGetProperty("owner", out _));
    }

    [Fact]
    public void SmallResult_IsCompleteAndSanitized_WithIdenticalJsonAndNoExecution()
    {
        PlanDefinition definition = PlanTestData.Definition();
        CallToolResult result = WorkflowResults.Success(new(1, PlanCanonical.Hash(definition), definition, new()));
        JsonElement data = result.StructuredContent!.Value;
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.Single(data.GetProperty("steps").EnumerateArray());
        Assert.Single(data.GetProperty("inputs").GetProperty("steps").EnumerateArray());
        Assert.Equal("Write", data.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("bindingRevision").ValueKind);
        CallToolResult error = WorkflowResults.Error("INVALID_ARGUMENT", "Correct the arguments.", new { pointer = "/steps" });
        Assert.True(error.IsError);
        Assert.Equal(error.StructuredContent!.Value.GetRawText(), ((TextContentBlock)error.Content[0]).Text);
        Assert.False(error.StructuredContent.Value.GetProperty("error").TryGetProperty("execution", out _));
    }
}
