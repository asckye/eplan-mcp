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
/// them. The full contract is snapshotted in <see cref="ToolContractSnapshotTests"/>.
/// </summary>
[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
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
    public void ToolNames_AreTheImplementedSet()
    {
        Assert.Equal(
            [AssetTools.GetTool, AssetTools.SearchTool, AssetTools.UpdateTool, CableTools.GetTool, CableDestructiveTools.RenumberTool, CheckTools.ListTool, CheckWriteTools.RunTool, ConnectionWriteTools.GenerateTool, ConnectionTools.ListTool, ConnectionDestructiveTools.NumberTool, DeviceTools.GetTool, DeviceTools.ListTool, DeviceWriteTools.PlaceTool, DeviceDestructiveTools.RenumberTool, DeviceWriteTools.UpdateTool, ExportTools.DxfTool, ImageExportTools.ImageTool, ProjectExportTools.LabelsTool, ProjectExportTools.PartsTool, ExportTools.PdfTool, ProjectExportTools.PxfTool, ProjectExportTools.UntranslatedTool, ImportTools.DxfTool, ImportTools.PxfTool, LanguageDestructiveTools.RemoveTool, LanguageWriteTools.SetTool, MacroTools.GetTool, MacroWriteTools.InsertTool, MacroTools.ListTool, MessageTools.ListTool, PackTools.BuildTool, PackTools.GetTool, PackTools.ImportTool, PackTools.ListTool, PageWriteTools.CreateTool, PageDestructiveTools.DeleteTool, PageTools.GetTool, PageTools.ListTool, PageDestructiveTools.RenumberTool, PageWriteTools.UpdateTool, MasterDataExportTools.PartTool, PartMasterDataTools.ImportTool, PartTools.ListTool, PartTools.SearchTool, PartMasterDataTools.SyncTool, PlaceholderWriteTools.ApplyTool, PlaceholderTools.ListTool, PlcTools.GetTool, ProjectTools.GetTool, ProjectTools.ListTool, ProjectDestructiveTools.TranslateTool, ReportWriteTools.UpdateTool, RevisionTools.ListTool, SessionTools.SelectTool, SessionTools.StatusTool, TerminalDestructiveTools.RenumberTool, TerminalTools.GetTool, TerminalTools.ListTool, UsageTools.UsageTool, MasterDataExportTools.TranslationTool],
            Tools.Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTool_FollowsTheNamingRule()
    {
        // terminal_strip is the one resource of two words in the vocabulary (mcp-tool-design 3.2).
        Assert.All(Tools, t => Assert.Matches("^eplan_([a-z]+|terminal_strip)_[a-z]+$", t.Name));
    }

    [Fact]
    public void EveryArgumentName_IsSnakeCase()
    {
        // mcp-tool-design 5: response_format, name_prefix; the C# parameter names become the schema names as they are.
        Assert.All(Tools, t => Assert.All(
            t.InputSchema.GetProperty("properties").EnumerateObject(),
            p => Assert.Matches("^[a-z]+(_[a-z]+)*$", p.Name)));
    }

    [Fact]
    public void ResponseFormat_IsAnEnumDefaultingToConcise()
    {
        Assert.All(Tools.Where(t => t.InputSchema.GetProperty("properties").TryGetProperty("response_format", out _)), t =>
        {
            JsonElement format = t.InputSchema.GetProperty("properties").GetProperty("response_format");
            Assert.Equal([ToolArguments.Concise, ToolArguments.Detailed], Enum(format));
            Assert.Equal(ToolArguments.Concise, format.GetProperty("default").GetString());
        });
        Assert.Contains(Tools, t => t.Name == ProjectTools.GetTool && t.InputSchema.GetProperty("properties").TryGetProperty("response_format", out _));
        Assert.Contains(Tools, t => t.Name == PageTools.GetTool && t.InputSchema.GetProperty("properties").TryGetProperty("response_format", out _));
    }

    [Fact]
    public void PageGet_RequiresOnlyThePage()
    {
        JsonElement schema = Tools.Single(t => t.Name == PageTools.GetTool).InputSchema;

        Assert.Equal(["page"], schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
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
        // eplan_tool_usage's list of tool names grows with every tool, so it is not counted (4.2).
        Assert.All(Tools, t => Assert.InRange(t.Description!.Length + t.InputSchema.GetRawText().Length - ToolNames(t), 1, 1000));
    }

    private static int ToolNames(Tool tool) =>
        tool.Name == UsageTools.UsageTool ? tool.InputSchema.GetProperty("properties").GetProperty("tool").GetProperty("enum").GetRawText().Length : 0;

    [Fact]
    public void EveryTool_HasUsageWithAllFourTopics()
    {
        // A tool without usage text, or a usage file without a tool, fails here (mcp-tool-design 10).
        Assert.Equal(Tools.Select(t => t.Name).Order(StringComparer.Ordinal), ToolUsageCatalog.Usage.Keys.Order(StringComparer.Ordinal));
        Assert.All(ToolUsageCatalog.Usage.Values, sections => Assert.Equal(ToolUsageCatalog.Topics.Order(StringComparer.Ordinal), sections.Keys.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void ToolUsageSchema_EnumeratesEveryToolAndTopic()
    {
        JsonElement properties = Tools.Single(t => t.Name == UsageTools.UsageTool).InputSchema.GetProperty("properties");

        Assert.Equal(Tools.Select(t => t.Name).Order(StringComparer.Ordinal), Enum(properties.GetProperty("tool")).Order(StringComparer.Ordinal));
        Assert.Equal(ToolUsageCatalog.Topics, Enum(properties.GetProperty("topic")));
    }

    [Fact]
    public void ServerInstructions_StayWithinTheBudget()
    {
        Assert.InRange(_options.ServerInstructions!.Length, 1, 1200);
    }

    private static IEnumerable<string?> Enum(JsonElement property) => property.GetProperty("enum").EnumerateArray().Select(v => v.GetString());
}
