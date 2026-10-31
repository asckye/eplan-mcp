using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ApiTryContractTests
{
    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"apiTry\":\"off\"}", false)]
    [InlineData("{\"apiTry\":\"on\"}", true)]
    public void OnlyExplicitFileSwitchEnablesTrials(string json, bool enabled)
    {
        var parsed = HostConfig.Parse(json, new(@"D:\test-data"));
        Assert.Null(parsed.Problem);
        Assert.Equal(enabled, parsed.Config!.ApiTry == ActionRunSwitch.On);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("\"yes\"")]
    public void InvalidSwitchCannotEnableTrials(string value) => Assert.Null(HostConfig.Parse("{\"apiTry\":" + value + "}", new(@"D:\test-data")).Config);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ToolAndUsageAreAbsentWhenOffOrCore(bool enabled, bool core)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(HostConfig.Defaults(new(@"D:\test-data")) with { ApiTry = enabled ? ActionRunSwitch.On : ActionRunSwitch.Off });
        services.AddSingleton(new ToolProfileChoice(core ? ToolProfile.Core : ToolProfile.Full));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        var tools = provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!;
        Assert.Equal(ReviewedToolContract.For(core ? ToolProfile.Core : ToolProfile.Full, actionRun: false, apiTry: enabled, actionTry: false).Length, tools.Count);
        Assert.Equal(enabled && !core, tools.Any(t => t.ProtocolTool.Name == ApiTryTools.Tool));
        var usage = tools.Single(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool;
        Assert.Equal(enabled && !core, usage.InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum").EnumerateArray().Any(n => n.GetString() == ApiTryTools.Tool));
        if (!enabled || core)
        { return; }
        var tool = tools.Single(t => t.ProtocolTool.Name == ApiTryTools.Tool).ProtocolTool;
        Assert.Equal((false, true, false, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.Equal(["group", "project", "parameters"], tool.InputSchema.GetProperty("required").EnumerateArray().Select(n => n.GetString()));
        Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
    }
}
