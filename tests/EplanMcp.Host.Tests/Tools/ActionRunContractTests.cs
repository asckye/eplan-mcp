using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class ActionRunContractTests
{
    [Fact]
    public void PendingR2Entries_AreAbsentFromSchemasUsageAndHostExecution()
    {
        JsonObject[] pending = [.. ActionRunCatalog.Document["pendingVerification"]!.AsArray().Select(item => item!.AsObject())];
        Assert.Equal(5, pending.Length);
        Assert.Contains("PENDING VERIFICATION", ActionRunCatalog.Document["pendingVerificationNote"]!.GetValue<string>(), StringComparison.Ordinal);
        using ServiceProvider services = Services(true, false);
        var tool = services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Single(t => t.ProtocolTool.Name == ActionRunTools.Tool).ProtocolTool;
        string[] exposed = [.. tool.InputSchema.GetProperty("properties").GetProperty("entry").GetProperty("enum").EnumerateArray().Select(item => item.GetString()!)];
        string[] output = [.. tool.OutputSchema!.Value.GetProperty("properties").GetProperty("entry").GetProperty("enum").EnumerateArray().Select(item => item.GetString()!)];
        Assert.Equal(ActionRunCatalog.Entries.Keys, exposed);
        Assert.Equal(exposed, output);
        Assert.Equal(15, exposed.Length);
        var catalog = new ActionRunCatalog();
        foreach (JsonObject rule in pending)
        {
            string name = rule["entry"]!.GetValue<string>();
            Assert.Equal("pending_verification", rule["status"]!.GetValue<string>());
            Assert.DoesNotContain(name, exposed);
            Assert.DoesNotContain(name, output);
            Assert.False(ActionRunCatalog.Entries.ContainsKey(name));
            Assert.True(ActionRunUsage.For(name).IsError);
            JsonObject example = ActionRunUsage.Example(rule);
            foreach (string entry in new[] { name, name.ToLowerInvariant(), name.ToUpperInvariant() }.Distinct(StringComparer.Ordinal))
            {
                Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeCallException>(() =>
                    catalog.Check(entry, "Line1", JsonSerializer.SerializeToElement(example["arguments"]))).Error.Reason);
            }
        }
    }

    [Theory]
    [InlineData("{}", "Off")]
    [InlineData("{\"actionRun\":\"off\"}", "Off")]
    [InlineData("{\"actionRun\":\"on\"}", "On")]
    public void Config_UsesTheExistingFileParserAndDefaultsOff(string text, string expected)
    {
        (HostConfig? config, string? problem) = HostConfig.Parse(text, new HostDataLocations(@"D:\test-data"));
        Assert.Null(problem);
        Assert.Equal(expected, config!.ActionRun.ToString());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("\"yes\"")]
    public void Config_InvalidSwitchIsRejected(string value) => Assert.Null(HostConfig.Parse("{\"actionRun\":" + value + "}", new(@"D:\test-data")).Config);

    [Theory]
    [InlineData(false, false, 79)]
    [InlineData(true, false, 80)]
    [InlineData(false, true, 25)]
    [InlineData(true, true, 25)]
    public void ToolAndUsage_AreRegisteredOnlyInFullWithSwitchOn(bool enabled, bool core, int count)
    {
        using ServiceProvider services = Services(enabled, core);
        McpServerOptions options = services.GetRequiredService<IOptions<McpServerOptions>>().Value;
        Assert.Equal(count, options.ToolCollection!.Count);
        Assert.Equal(enabled && !core, options.ToolCollection.Any(t => t.ProtocolTool.Name == ActionRunTools.Tool));
        var usage = options.ToolCollection.Single(t => t.ProtocolTool.Name == UsageTools.UsageTool).ProtocolTool;
        Assert.Equal(enabled && !core, usage.InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum").EnumerateArray().Any(n => n.GetString() == ActionRunTools.Tool));
    }

    [Fact]
    public void AnnotationsAndCompactSchema_PinTheHighestTierAndEveryWhitelistEntry()
    {
        using ServiceProvider services = Services(true, false);
        var tool = services.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Single(t => t.ProtocolTool.Name == ActionRunTools.Tool).ProtocolTool;
        Assert.Equal((false, true, false, false), (tool.Annotations!.ReadOnlyHint, tool.Annotations.DestructiveHint, tool.Annotations.IdempotentHint, tool.Annotations.OpenWorldHint));
        Assert.Equal(ActionRunCatalog.Entries.Keys, tool.InputSchema.GetProperty("properties").GetProperty("entry").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(15, tool.OutputSchema!.Value.GetProperty("properties").GetProperty("entry").GetProperty("enum").GetArrayLength());
        Assert.Equal(["entry", "project", "arguments", "dry_run"], tool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Equal("string", tool.InputSchema.GetProperty("properties").GetProperty("arguments").GetProperty("additionalProperties").GetProperty("type").GetString());
        Assert.False(tool.InputSchema.TryGetProperty("oneOf", out _));
        Assert.False(tool.InputSchema.TryGetProperty("$defs", out _));
        Assert.False(tool.InputSchema.TryGetProperty("allOf", out _));
        Assert.InRange(tool.Description!.Length + tool.InputSchema.GetRawText().Length, 1, 1000);
        TestContext.Current.TestOutputHelper?.WriteLine($"Description chars: {tool.Description!.Length}; input schema chars: {tool.InputSchema.GetRawText().Length}.");
    }

    [Fact]
    public void Usage_ListsEachEntryAndItsLimitations_AndRemainsHiddenWhenOff()
    {
        HostConfig on = HostConfig.Defaults(new(@"D:\test-data")) with { ActionRun = ActionRunSwitch.On };
        Assert.True(UsageTools.ToolUsage(ActionRunTools.Tool).IsError);
        foreach (string name in ActionRunCatalog.Entries.Keys)
        {
            var usage = UsageTools.ToolUsage(ActionRunTools.Tool, name, on);
            ToolResultAssert.Succeeded(usage);
            string text = usage.StructuredContent!.Value.GetProperty("text").GetString()!;
            Assert.Contains(name, text, StringComparison.Ordinal);
            Assert.Contains("Example:", text, StringComparison.Ordinal);
            Assert.Contains("Fixed:", text, StringComparison.Ordinal);
            Assert.Contains("| Name | Required | Allowed value |", text, StringComparison.Ordinal);
            Assert.Contains(ActionRunCatalog.Text(ActionRunCatalog.Entries[name], "effects"), text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryEntryExample_ValidatesAndExpandsOnlySingleValues()
    {
        var catalog = new ActionRunCatalog();
        foreach ((string name, JsonObject rule) in ActionRunCatalog.Entries)
        {
            JsonObject example = ActionRunUsage.Example(rule);
            var checkedArgs = catalog.Check(name, example["project"]?.GetValue<string>(), JsonSerializer.SerializeToElement(example["arguments"])).Args;
            Assert.All(checkedArgs.Parameters, p => Assert.False(p.Name is "LAYER" or "VISIBLE" or "PROPERTYID" or "PROPERTYVALUE"));
        }
    }

    [Theory]
    [InlineData("{\"LAYER\":\"EPLAN100\",\"VISIBLE\":\"0\"}", true)]
    [InlineData("{\"LAYER\":\"EPLAN100\",\"VISIBLE\":\"1\"}", true)]
    [InlineData("{\"LAYER\":[\"EPLAN100\",\"other\"],\"VISIBLE\":\"0\"}", false)]
    [InlineData("{\"LAYER1\":\"EPLAN100\",\"VISIBLE1\":\"0\"}", false)]
    [InlineData("{\"LAYER\":\"EPLAN100\",\"VISIBLE\":[0]}", false)]
    [InlineData("{\"LAYER\":[\"/x\"],\"VISIBLE\":\"0\"}", false)]
    [InlineData("{\"LAYER\":[\"a'b\"],\"VISIBLE\":\"0\"}", false)]
    public void Layer_UsesTheExactInputShape(string json, bool valid)
    {
        var catalog = new ActionRunCatalog();
        if (valid)
        {
            var args = catalog.Check("changelayer", "Line1", Json(json)).Args;
            Assert.Equal("LAYER1", args.Parameters[0].Name);
            Assert.Equal("VISIBLE1", args.Parameters[1].Name);
        }
        else
        { Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => catalog.Check("changelayer", "Line1", Json(json))).Error.Reason); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"EXPORTFILE\":\"parts.xml\"}")]
    [InlineData("{\"EXPORTFILE\":\"parts.xml\",\"PROPERTYID\":\"22900\"}")]
    [InlineData("{\"EXPORTFILE\":\"parts.xml\",\"PROPERTYVALUE\":\"ABB\"}")]
    [InlineData("{\"EXPORTFILE\":\"parts.xml\",\"PROPERTYID\":[\"22900\"],\"PROPERTYVALUE\":\"ABB\"}")]
    [InlineData("{\"EXPORTFILE\":\"parts.xml\",\"PROPERTYID\":\"22900\",\"PROPERTYVALUE\":\"*\"}")]
    public void Parts_RequiresBothExactStringFilters(string json) => Assert.Equal(ErrorReason.InvalidArgument,
        Assert.Throws<BridgeCallException>(() => new ActionRunCatalog().Check("partsmanagementapi/EXPORTMANUFACTURERS", null, Json(json))).Error.Reason);

    [Theory]
    [InlineData("D:\\escape.xml")]
    [InlineData("../escape")]
    [InlineData("CON.xml")]
    [InlineData("COM¹.xml")]
    [InlineData("trailing.")]
    [InlineData("bad'quote")]
    [InlineData("bad.txt")]
    public void Files_RefusePathTricksAndUnverifiedExtensions(string file) => Assert.Equal(ErrorReason.InvalidArgument,
        Assert.Throws<BridgeCallException>(() => new ActionRunCatalog().Check("XEsUserPropertiesExportAction", "Line1", JsonSerializer.SerializeToElement(new { XMLFile = file }))).Error.Reason);

    [Fact]
    public void FileExtensionAndText_ArePreservedExactly()
    {
        ActionRunArgs args = new ActionRunCatalog().Check("graphicallayertable/EXPORT", "Line1", Json("{\"EXPORTFILE\":\"层 表\"}")).Args;
        Assert.Equal("层 表.elc", args.Parameters.Single().Value);
        Assert.Throws<BridgeCallException>(() => new ActionRunCatalog().Check("ProjectAction", null, Json("{}")));
    }

    [Fact]
    public void Approval_ListsAllParametersAndFingerprintUndoLimitations()
    {
        var plan = new WritePlan { Project = "Line1", Changes = [new() { Action = "run", Kind = "action", Name = "changelayer", NewValue = "[{\"name\":\"PROJECTNAME\",\"value\":\"D:\\\\scratch\\\\Line1\"},{\"name\":\"LAYER1\",\"value\":\"EPLAN100\"},{\"name\":\"VISIBLE1\",\"value\":\"0\"}]" }], Details = new() { ["effects"] = "Layer.eod/eox may differ after rollback." } };
        var question = new ApprovalQuestion("r", ActionRunTools.Tool, "h", plan, [], DateTimeOffset.UnixEpoch) { Tier = ToolTier.Write, Eplan = null };
        foreach (string culture in new[] { "en-US", "zh-CN" })
        {
            ApprovalAskArgs text = ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture));
            Assert.Contains("LAYER1=EPLAN100", text.Changes.Single(), StringComparison.Ordinal);
            Assert.Contains("VISIBLE1=0", text.Changes.Single(), StringComparison.Ordinal);
            Assert.Contains("Ctrl+Z", text.Note, StringComparison.Ordinal);
        }
    }
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
    private static ServiceProvider Services(bool enabled, bool core)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.ClearProviders());
        services.AddSingleton(HostConfig.Defaults(new(@"D:\test-data")) with { ActionRun = enabled ? ActionRunSwitch.On : ActionRunSwitch.Off });
        services.AddSingleton(new ToolProfileChoice(core ? ToolProfile.Core : ToolProfile.Full));
        services.AddEplanMcp();
        return services.BuildServiceProvider();
    }
}
