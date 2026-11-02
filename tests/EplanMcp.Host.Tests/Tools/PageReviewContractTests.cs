using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Medium")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class PageReviewContractTests
{
    [Fact]
    public async Task RegisteredReadTool_UsesFakeBridgeAndPublishedSchema()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        await using ServiceProvider provider = services.BuildServiceProvider();
        Tool tool = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Single(t => t.ProtocolTool.Name == PageReviewTools.Tool).ProtocolTool;
        Assert.Equal((true, false, true, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.Equal(["project", "page"], tool.InputSchema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        JsonElement properties = tool.InputSchema.GetProperty("properties");
        Assert.Equal(PageReviewTools.Checks, properties.GetProperty("checks").GetProperty("items").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(0.5, properties.GetProperty("tolerance_mm").GetProperty("default").GetDouble());
        Assert.Equal(50, properties.GetProperty("limit").GetProperty("default").GetInt32());
        Assert.False(properties.GetProperty("expected").GetProperty("additionalProperties").GetBoolean());
        Assert.Contains(PageReviewTools.Tool, ToolProfiles.Core);
        Assert.InRange(ToolProfiles.Core.Count, 1, ToolProfiles.CoreLimit);
        Assert.NotNull(tool.OutputSchema);
        var bridge = new FakeBridgeClient();
        bridge.Handlers[OperationNames.PageReview] = _ => JsonNode.Parse("""{"project":"Resolved","page":"=F+A1/1","issues":[],"actual":{"devices":[],"connections":[],"cables":[],"interruptions":[],"texts":[],"messages":[]}}""")!.AsObject();
        CallToolResult result = await PageReviewTools.ReviewAsync(bridge, new ManualTime(DateTimeOffset.UnixEpoch), "Line1", "=F+A1/1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsError == true);
        Assert.Equal("Resolved", result.StructuredContent!.Value.GetProperty("project").GetString());
        foreach (JsonElement required in tool.OutputSchema.Value.GetProperty("required").EnumerateArray())
        { Assert.True(result.StructuredContent.Value.TryGetProperty(required.GetString()!, out _)); }
    }
    [Theory]
    [InlineData(ErrorReason.PageNotFound)]
    [InlineData(ErrorReason.ProjectNotOpen)]
    [InlineData(ErrorReason.EplanBusy)]
    [InlineData(ErrorReason.Timeout)]
    public async Task FakeBridgeFailures_PreserveReasonAndReadOnlyOutcome(string reason)
    {
        var bridge = new FakeBridgeClient { Failure = new() { Reason = reason, Message = "Use eplan_page_list and retry." } };
        CallToolResult result = await PageReviewTools.ReviewAsync(bridge, new ManualTime(DateTimeOffset.UnixEpoch), "Line1", "=F+A1/1", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        using JsonDocument body = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        JsonElement error = body.RootElement.GetProperty("error");
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, error.GetProperty("execution").ValueKind);
    }
}
