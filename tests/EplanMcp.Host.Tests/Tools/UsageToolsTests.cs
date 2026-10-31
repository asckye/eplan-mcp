using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed class UsageToolsTests
{
    private static readonly bool[] _switches = [false, true];

    public static IEnumerable<TheoryDataRow<bool, bool, bool, bool>> Configurations() =>
        from core in _switches
        from actionRun in _switches
        from apiTry in _switches
        from actionTry in _switches
        select new TheoryDataRow<bool, bool, bool, bool>(core, actionRun, apiTry, actionTry);

    [Theory]
    [MemberData(nameof(Configurations))]
    public void SchemaAndValidation_MatchTheReviewedProfileAcrossSessions(bool core, bool actionRun, bool apiTry, bool actionTry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(HostConfig.Defaults(new(@"D:\test-data")) with
        {
            ActionRun = actionRun ? ActionRunSwitch.On : ActionRunSwitch.Off,
            ApiTry = apiTry ? ActionRunSwitch.On : ActionRunSwitch.Off,
            ActionTry = actionTry ? ActionRunSwitch.On : ActionRunSwitch.Off,
        });
        ToolProfile profile = core ? ToolProfile.Core : ToolProfile.Full;
        services.AddSingleton(new ToolProfileChoice(profile));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        string[] expected = ReviewedToolContract.For(profile, actionRun, apiTry, actionTry);
        McpServerTool? sharedUsage = null;
        for (int session = 0; session < 3; session++)
        {
            McpServerOptions options = provider.GetRequiredService<IOptionsFactory<McpServerOptions>>().Create(Options.DefaultName);
            UsageTools.Configure(options);
            Assert.Equal(expected, options.ToolCollection!.Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal));
            McpServerTool usage = options.ToolCollection!.First(t => t.ProtocolTool.Name == UsageTools.UsageTool);
            if (sharedUsage is not null)
            {
                Assert.Same(sharedUsage, usage);
            }
            sharedUsage = usage;
            Assert.Equal(expected, usage.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum")
                .EnumerateArray().Select(n => n.GetString()));
            var configured = Options.Create(options);
            Assert.All(expected, name => ToolResultAssert.Succeeded(UsageTools.ToolUsage(configured, name)));
            string violations = Violations(UsageTools.ToolUsage(configured, "missing", "invalid"));
            Assert.Contains($"'tool' must be one of: {string.Join(", ", expected)}.", violations, StringComparison.Ordinal);
            Assert.Contains("'topic' must be one of: examples, workflow, fields, errors;", violations, StringComparison.Ordinal);
            Assert.All(ReviewedToolContract.Names.Except(expected, StringComparer.Ordinal), name =>
                Assert.True(UsageTools.ToolUsage(configured, name).IsError));
        }
    }

    [Fact]
    public void Validation_UsesRegistrationEvenWhenUsageTextStillExists()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<McpServerOptions> options = provider.GetRequiredService<IOptions<McpServerOptions>>();
        McpServerTool tool = options.Value.ToolCollection!.First(t => t.ProtocolTool.Name == ProjectTools.ListTool);
        options.Value.ToolCollection!.Remove(tool);
        UsageTools.Configure(options.Value);

        Assert.True(ToolUsageCatalog.Usage.ContainsKey(ProjectTools.ListTool));
        Assert.True(UsageTools.ToolUsage(options, ProjectTools.ListTool).IsError);
        Assert.DoesNotContain(ProjectTools.ListTool, Violations(UsageTools.ToolUsage(options, null!)), StringComparison.Ordinal);
        options.Value.ToolCollection!.Add(tool);
        UsageTools.Configure(options.Value);
        ToolResultAssert.Succeeded(UsageTools.ToolUsage(options, ProjectTools.ListTool));
    }

    [Fact]
    public void ApiTryUsage_IsOnlyAvailableWhenExplicitlyEnabled_AndExplainsTheSlice()
    {
        Assert.True(Usage(ApiTryTools.Tool).IsError);
        var config = EplanMcp.Host.Safety.HostConfig.Defaults(new(@"D:\test-data")) with { ApiTry = EplanMcp.Host.Safety.ActionRunSwitch.On };
        string text = Structured(Usage(ApiTryTools.Tool, config: config)).GetProperty("text").GetString()!;
        Assert.Contains("not_dispatchable_yet", text, StringComparison.Ordinal);
        Assert.Contains("OUTCOME_UNKNOWN", text, StringComparison.Ordinal);
        Assert.Contains("unverified", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionTryUsage_IsOptInAndExplainsUnverifiedScratchAndUnknownOutcome()
    {
        Assert.True(Usage(ActionTryTools.Tool).IsError);
        var config = EplanMcp.Host.Safety.HostConfig.Defaults(new(@"D:\test")) with { ActionTry = EplanMcp.Host.Safety.ActionRunSwitch.On };
        string text = Structured(Usage(ActionTryTools.Tool, config: config)).GetProperty("text").GetString()!;
        foreach (string token in new[] { "unverified", "scratch", "OUTCOME_UNKNOWN", "Ctrl+Z", "30", "AddParameter" })
        { Assert.Contains(token, text, StringComparison.Ordinal); }
        Assert.True(Usage(ActionTryTools.Tool, config: config, profile: new(ToolProfile.Core)).IsError);
    }
    [Fact]
    public void ProjectCreateUsage_ExplainsTheNewFolderCompatibilityAndUnknownOutcome()
    {
        string text = Structured(Usage(ProjectCreateTools.CreateTool)).GetProperty("text").GetString()!;
        foreach (string word in new[] { "basic_project", "backup", "empty", "IsCompatible", "OUTCOME_UNKNOWN", "Ctrl+Z", "2 GB" })
        { Assert.Contains(word, text, StringComparison.Ordinal); }
    }

    [Fact]
    public void ToolUsage_WithoutTopic_ReturnsAllFourSectionsInOrder()
    {
        CallToolResult result = Usage(ProjectTools.ListTool);

        JsonElement data = Structured(result);
        Assert.Equal(ProjectTools.ListTool, data.GetProperty("tool").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("topic").ValueKind);
        Assert.Equal(["## Examples", "## Workflow", "## Fields", "## Errors"], Headings(data.GetProperty("text").GetString()!));
        Assert.Equal("Usage of eplan_project_list: examples, workflow, fields, errors.", Text(result, 0));
        Assert.Equal(data.GetRawText(), Text(result, 1));
    }

    [Fact]
    public void ToolUsage_WithTopic_ReturnsOnlyThatSection()
    {
        CallToolResult result = Usage(SessionTools.StatusTool, ToolUsageCatalog.Errors);

        JsonElement data = Structured(result);
        string text = data.GetProperty("text").GetString()!;
        Assert.Equal(ToolUsageCatalog.Errors, data.GetProperty("topic").GetString());
        Assert.Equal(["## Errors"], Headings(text));
        Assert.Contains(ErrorReason.EplanNotConnected, text, StringComparison.Ordinal);
        Assert.Equal("Usage of eplan_session_status: errors.", Text(result, 0));
    }

    [Fact]
    public void ListImportUsage_ExplainsProjectScopeBackupAndUnknownUndo()
    {
        string usage = Structured(Usage(ListImportTools.ImportTool)).GetProperty("text").GetString()!;
        Assert.Contains("parts accept XML only", usage, StringComparison.Ordinal);
        Assert.Contains("Undo after commit is unverified", usage, StringComparison.Ordinal);
        Assert.Contains("300 seconds including backup", usage, StringComparison.Ordinal);
        Assert.Contains("not imported rows", usage, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolUsage_UnknownToolAndTopic_AreReportedTogetherWithTheValidValues()
    {
        CallToolResult result = Usage("eplan_page_lst", "usage");

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        JsonElement error = JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        string violations = error.GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Contains($"'tool' must be one of: {string.Join(", ", ReviewedToolContract.For(actionRun: false, apiTry: false, actionTry: false))}.", violations, StringComparison.Ordinal);
        Assert.Contains("'topic' must be one of: examples, workflow, fields, errors;", violations, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Examples")]
    [InlineData("")]
    public void ToolUsage_TopicNotExactlyAnEnumValue_IsInvalidArgument(string topic)
    {
        Assert.True(Usage(ProjectTools.ListTool, topic).IsError);
    }

    [Fact]
    public void PageListUsage_ListsEveryPageTypeTheBridgeSuggests()
    {
        // The Bridge's INVALID_ARGUMENT for page_type names these and points to this section for the rest.
        string fields = ToolUsageCatalog.Usage[PageTools.ListTool][ToolUsageCatalog.Fields];
        string[] listed = [.. fields.Split(["`page_type` values"], StringSplitOptions.None)[1].Split([',', ':', '.', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries)];

        Assert.All(PageListOperation.CommonPageTypes, type => Assert.Contains(type, listed));
    }

    [Fact]
    public void UsageTexts_StayWithinTheBudget()
    {
        // The result carries the text twice (structured and JSON text block); ~1.5k tokens per tool keeps all four
        // topics well within the ~5k token default budget (mcp-tool-design 7.2).
        Assert.All(ToolUsageCatalog.Usage, usage => Assert.InRange(usage.Value.Values.Sum(section => section.Length), 1, 6000));
    }

    [Fact]
    public void Parse_FourSectionsInOrder_SplitsThemWithTheirHeadings()
    {
        IReadOnlyDictionary<string, string> sections =
            ToolUsageCatalog.Parse("t.md", "## Examples\r\n\r\n    {}\r\n\r\n## Workflow\r\nw\r\n### Detail\r\nd\r\n## Fields\r\nf\r\n## Errors\r\ne\r\n");

        Assert.Equal("## Examples\n\n    {}", sections[ToolUsageCatalog.Examples]);
        Assert.Equal("## Workflow\nw\n### Detail\nd", sections[ToolUsageCatalog.Workflow]);
        Assert.Equal("## Errors\ne", sections[ToolUsageCatalog.Errors]);
    }

    [Theory]
    [InlineData("## Examples\nx\n## Workflow\nx\n## Fields\nx\n", "found: Examples, Workflow, Fields.")]
    [InlineData("## Workflow\nx\n## Examples\nx\n## Fields\nx\n## Errors\nx\n", "found: Workflow, Examples")]
    [InlineData("## Examples\nx\n## Workflow\nx\n## Fields\nx\n## Errors\nx\n## Notes\nx\n", "Errors, Notes.")]
    [InlineData("## Examples\nx\n## Workflow\nx\n## Fields\n\n## Errors\nx\n", "t.md needs")]
    [InlineData("# eplan_x\n## Examples\nx\n## Workflow\nx\n## Fields\nx\n## Errors\nx\n", "t.md needs")]
    [InlineData("", "found: none.")]
    public void Parse_BrokenStructure_Throws(string markdown, string message)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => ToolUsageCatalog.Parse("t.md", markdown));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static CallToolResult Usage(string tool, string? topic = null, HostConfig? config = null, ToolProfileChoice? profile = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(config ?? HostConfig.Defaults(new(@"D:\test-data")));
        services.AddSingleton(profile ?? new(ToolProfile.Full));
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        return UsageTools.ToolUsage(provider.GetRequiredService<IOptions<McpServerOptions>>(), tool, topic);
    }

    private static string[] Headings(string text) =>
        [.. text.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal))];

    private static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    private static string Violations(CallToolResult result)
    {
        using JsonDocument document = JsonDocument.Parse(Text(result, 0));
        return document.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("violations").GetString()!;
    }

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }
}
