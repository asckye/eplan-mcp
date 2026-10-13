using System.Text.Json;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// The rules of mcp-tool-design 12 that can be checked mechanically, on the tools exactly as <c>serve</c> registers
/// them. Snapshot tests of the full contract arrive with Verify in M2.
/// </summary>
[Trait("Size", "Small")]
public sealed class ToolContractTests : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly McpServerOptions _options;

    public ToolContractTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        _services = services.BuildServiceProvider();
        _options = _services.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    public void Dispose() => _services.Dispose();

    private Tool[] Tools => [.. _options.ToolCollection!.Select(t => t.ProtocolTool)];

    [Fact]
    public void ToolNames_AreTheM1Set()
    {
        Assert.Equal([ProjectTools.ListTool, SessionTools.StatusTool], Tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTool_FollowsTheNamingRule()
    {
        Assert.All(Tools, t => Assert.Matches("^eplan_[a-z]+_[a-z]+$", t.Name));
    }

    [Fact]
    public void EveryTool_SetsTitleAndAllFourHintsExplicitly()
    {
        Assert.All(Tools, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
            Assert.NotNull(t.Annotations);
            Assert.NotNull(t.Annotations.ReadOnlyHint);
            Assert.NotNull(t.Annotations.DestructiveHint);
            Assert.NotNull(t.Annotations.IdempotentHint);
            Assert.False(t.Annotations.OpenWorldHint);
        });
    }

    [Fact]
    public void ReadOnlyTools_AreNotDestructiveAndAreIdempotent()
    {
        Assert.All(Tools.Where(t => t.Annotations!.ReadOnlyHint == true), t =>
        {
            Assert.False(t.Annotations!.DestructiveHint);
            Assert.True(t.Annotations.IdempotentHint);
        });
    }

    [Fact]
    public void EveryInputSchema_RejectsUnknownArguments()
    {
        Assert.All(Tools, t =>
        {
            Assert.Equal("object", t.InputSchema.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.False, t.InputSchema.GetProperty("additionalProperties").ValueKind);
        });
    }

    [Fact]
    public void EveryTool_DeclaresAnOutputSchema()
    {
        Assert.All(Tools, t => Assert.Equal("object", t.OutputSchema!.Value.GetProperty("type").GetString()));
    }

    [Fact]
    public void Descriptions_StayWithinTheBudget()
    {
        // ~250 tokens for description plus parameters (mcp-tool-design 4.2); 4 characters per token is a fair English estimate.
        Assert.All(Tools, t => Assert.InRange(t.Description!.Length + t.InputSchema.GetRawText().Length, 1, 1000));
    }

    [Fact]
    public void ServerInstructions_StayWithinTheBudget()
    {
        Assert.InRange(_options.ServerInstructions!.Length, 1, 1200);
    }
}
