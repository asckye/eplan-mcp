using System.Text.Json;
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
    [InlineData("Function.IsPointInsideLogicalArea", "\"pnt\":{\"x\":1,\"y\":2}", true)]
    [InlineData("Function.IsPointInsideLogicalArea", "\"pnt\":{\"x\":1,\"y\":1e999}", false)]
    [InlineData("Function.IsPointInsideLogicalArea", "\"pnt\":{\"x\":1,\"y\":2,\"z\":3}", false)]
    [InlineData("Function.IsPointInsideLogicalArea", "\"pnt\":{\"x\":1,\"x\":2,\"y\":3}", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "\"nKind\":\"Manual\"", true)]
    [InlineData("Page.GetWriteProtectionFlagSet", "\"nKind\":\"manual\"", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "\"nKind\":2", false)]
    [InlineData("Page.GetWriteProtectionFlagSet", "\"value\":\"Manual\"", false)]
    [InlineData("Graphics.PlaceHolder.ApplyRecord", "\"strRecordName\":\"r\"", true)]
    [InlineData("Graphics.PlaceHolder.ApplyRecord", "\"strRecordName\":\"r\",\"bApplyPageAndProjectData\":false", true)]
    [InlineData("Graphics.PlaceHolder.ApplyRecord", "\"strRecordName\":\"r\",\"bApplyPageAndProjectData\":\"false\"", false)]
    public void MethodShapeValidationMatchesGeneratedSignature(string member, string fields, bool valid)
    {
        using JsonDocument json = JsonDocument.Parse("{\"target\":{\"project\":\"Scratch\",\"page\":\"=A/1\",\"type\":1,\"oid\":1,\"digest\":\"" + new string('a', 64) + "\"}," + fields + "}");
        Assert.Equal(valid, ApiTryTools.MethodParameters("O:Eplan.EplApi.DataModel." + member, json.RootElement));
    }
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
        JsonElement shapes = tool.InputSchema.GetProperty("properties").GetProperty("parameters").GetProperty("additionalProperties").GetProperty("oneOf");
        JsonElement point = Assert.Single(shapes.EnumerateArray(), s => s.TryGetProperty("properties", out JsonElement p) && p.TryGetProperty("x", out _));
        Assert.Equal(["x", "y"], point.GetProperty("required").EnumerateArray().Select(n => n.GetString()));
        Assert.False(point.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(3, tool.OutputSchema!.Value.GetProperty("properties").GetProperty("value").GetProperty("oneOf").GetArrayLength());
    }
}
