using System.Text.Json;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class UsageToolsTests
{
    [Fact]
    public void ToolUsage_WithoutTopic_ReturnsAllFourSectionsInOrder()
    {
        CallToolResult result = UsageTools.ToolUsage(ProjectTools.ListTool);

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
        CallToolResult result = UsageTools.ToolUsage(SessionTools.StatusTool, ToolUsageCatalog.Errors);

        JsonElement data = Structured(result);
        string text = data.GetProperty("text").GetString()!;
        Assert.Equal(ToolUsageCatalog.Errors, data.GetProperty("topic").GetString());
        Assert.Equal(["## Errors"], Headings(text));
        Assert.Contains(ErrorReason.EplanNotConnected, text, StringComparison.Ordinal);
        Assert.Equal("Usage of eplan_session_status: errors.", Text(result, 0));
    }

    [Fact]
    public void ToolUsage_UnknownToolAndTopic_AreReportedTogetherWithTheValidValues()
    {
        CallToolResult result = UsageTools.ToolUsage("eplan_page_lst", "usage");

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        JsonElement error = JsonDocument.Parse(Text(result, 0)).RootElement.GetProperty("error");
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        string violations = error.GetProperty("metadata").GetProperty("violations").GetString()!;
        Assert.Contains("'tool' must be one of: eplan_check_list, eplan_connection_list, eplan_device_get, eplan_device_list, eplan_macro_list, eplan_message_list, eplan_page_get, eplan_page_list, eplan_part_list, eplan_project_get, eplan_project_list, eplan_session_select, eplan_session_status, eplan_tool_usage.", violations, StringComparison.Ordinal);
        Assert.Contains("'topic' must be one of: examples, workflow, fields, errors;", violations, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Examples")]
    [InlineData("")]
    public void ToolUsage_TopicNotExactlyAnEnumValue_IsInvalidArgument(string topic)
    {
        Assert.True(UsageTools.ToolUsage(ProjectTools.ListTool, topic).IsError);
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

    private static string[] Headings(string text) =>
        [.. text.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal))];

    private static string Text(CallToolResult result, int index) => ((TextContentBlock)result.Content[index]).Text;

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value;
    }
}
