using System.Text.Json;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class CabinetContractTests
{
    [Theory]
    [InlineData(CabinetTools.ListTool, true)]
    [InlineData(CabinetTools.BindTool, false)]
    public void CabinetTools_HaveExactAnnotationsAndFullProfileOnly(string name, bool readOnly)
    {
        Tool tool = Assert.Single(Tools(ToolProfile.Full), t => t.Name == name);
        Assert.Equal((readOnly, false, true, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.DoesNotContain(Tools(ToolProfile.Core), t => t.Name == name);
        Assert.NotNull(tool.OutputSchema);
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("project", tool.InputSchema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public void List_DefaultAndHardBudgets_ArePartOfTheContract()
    {
        JsonElement input = Assert.Single(Tools(ToolProfile.Full), t => t.Name == CabinetTools.ListTool).InputSchema.GetProperty("properties");
        Assert.Equal(50, input.GetProperty("limit").GetProperty("default").GetInt32());
        Assert.Equal(200, input.GetProperty("limit").GetProperty("maximum").GetInt32());
        Assert.Equal(5000, input.GetProperty("max_tokens").GetProperty("default").GetInt32());
        Assert.Equal(20000, input.GetProperty("max_tokens").GetProperty("maximum").GetInt32());
        Assert.Equal(1, input.GetProperty("max_tokens").GetProperty("minimum").GetInt32());
    }

    private static Tool[] Tools(ToolProfile profile)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(new ToolProfileChoice(profile));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        return [.. provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Select(t => t.ProtocolTool)];
    }
}
