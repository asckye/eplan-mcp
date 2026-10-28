using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// eplan_project_get, eplan_page_list and eplan_page_get as the Host runs them, through a real BridgeClient and named
/// pipe into Bridge.Core's operations. Only EPLAN is faked: the project catalog and the readers.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectPageInteropTests : IAsyncDisposable
{
    private static readonly OpenProject _line1 = new() { Name = "Line1", Path = @"D:\Projects\Line1", Current = true };

    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly Catalog _catalog = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;
    private readonly DrawingReader _drawings = new();
    private readonly PageEditor _editor = new();

    public ProjectPageInteropTests()
    {
        var pages = new PageReader();
        IOperation[] operations =
        [
            new ProjectListOperation(_catalog),
            new ProjectGetOperation(_catalog, new ProjectReader()),
            new PageListOperation(_catalog, pages),
            new PageGetOperation(_catalog, pages),
            new PageOpenOperation(_catalog, _editor),
            new PageDrawingGetOperation(_catalog, _drawings),
        ];
        var router = new OperationRouter(operations, new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".0123456789abcdef",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "0.1.0-test", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task Handshake_AnnouncesTheNewOperations()
    {
        BridgeStatus status = await _client.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [OperationNames.OperationCancel, OperationNames.OutcomeGet, OperationNames.PageDrawingGet, OperationNames.PageGet, OperationNames.PageList, OperationNames.PageOpen, OperationNames.ProjectGet, OperationNames.ProjectList],
            status.Handshake!.Operations);
    }

    [Fact]
    public async Task PageOpen_ExplicitProjectAndExactPage_IgnoreCurrentProjectAndPrefixNeighbors()
    {
        _catalog.Projects = [_line1, new() { Name = "Line2", Path = @"D:\Projects\Line2" }];
        for (int i = 0; i < 2; i++)
        {
            JsonElement data = Structured(await PageOpenTools.OpenAsync(_client, "Line2", "=A/1", Token));
            Assert.Equal(("Line2", "=A/1", true), (data.GetProperty("project").GetString(), data.GetProperty("page").GetString(), data.GetProperty("editor_opened").GetBoolean()));
            Assert.False(data.TryGetProperty("execution", out _));
        }
        Assert.Equal(Enumerable.Repeat((@"D:\Projects\Line2", "=A/1"), 2), _editor.Calls);
        Assert.True(_line1.Current);
    }

    [Theory]
    [InlineData("Line1", "=A/1", ErrorReason.PageNotFound)]
    [InlineData("Line2", "=a/1", ErrorReason.PageNotFound)]
    [InlineData("Line2", "=A/", ErrorReason.PageNotFound)]
    [InlineData("Line2", "=A/9", ErrorReason.PageNotFound)]
    [InlineData("missing", "=A/1", ErrorReason.ProjectNotOpen)]
    public async Task PageOpen_MissingOrWrongScope_ReturnsActionableReadError(string project, string page, string reason)
    {
        _catalog.Projects = [_line1, new() { Name = "Line2", Path = @"D:\Projects\Line2" }];
        JsonElement error = Error(await PageOpenTools.OpenAsync(_client, project, page, Token));
        Assert.Equal(reason, error.GetProperty("reason").GetString());
        Assert.False(error.TryGetProperty("execution", out JsonElement execution) && execution.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task PageOpen_DuplicateProjectNames_RequiresExplicitPath()
    {
        _catalog.Projects = [_line1, new() { Name = "Line1", Path = @"D:\Projects\Line2" }];
        Assert.Equal(ErrorReason.AmbiguousName, Error(await PageOpenTools.OpenAsync(_client, "Line1", "=A/1", Token)).GetProperty("reason").GetString());
        Assert.Empty(_editor.Calls);
        Assert.Equal("Line1", Structured(await PageOpenTools.OpenAsync(_client, @"D:\Projects\Line2.elk", "=A/1", Token)).GetProperty("project").GetString());
    }

    private sealed class PageEditor : IPageEditor
    {
        public List<(string Path, string Page)> Calls { get; } = [];
        public string? Open(string projectPath, string pageName)
        {
            Calls.Add((projectPath, pageName));
            string[] pages = projectPath == @"D:\Projects\Line2" ? ["=A/1", "=A/10"] : ["=A/10"];
            return pages.FirstOrDefault(name => string.Equals(name, pageName, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ProjectGet_CarriesCountsTypesAndUnicodePropertiesOverThePipe()
    {
        _catalog.Projects = [_line1];

        JsonElement data = Structured(await ProjectTools.ProjectGetAsync(_client, null, ToolArguments.Detailed, "zh_CN", Token));

        Assert.Equal(@"D:\Projects\Line1", data.GetProperty("project").GetProperty("path").GetString());
        Assert.Equal(4, data.GetProperty("counts").GetProperty("pages").GetInt32());
        Assert.Equal(("Circuit", 2), (data.GetProperty("pagesByType")[0].GetProperty("type").GetString(), data.GetProperty("pagesByType")[0].GetProperty("count").GetInt32()));
        JsonElement property = data.GetProperty("properties")[1];
        Assert.Equal((10901, 2, "附加字段 二"), (property.GetProperty("id").GetInt32(), property.GetProperty("index").GetInt32(), property.GetProperty("value").GetString()));
    }

    [Fact]
    public async Task NoProjectOpen_IsProjectNotOpenForEveryTool()
    {
        CallToolResult[] results =
        [
            await ProjectTools.ProjectGetAsync(_client, null, ToolArguments.Concise, null, cancellationToken: Token),
            await PageTools.PageListAsync(_client, null, null, null, null, null, 50, null, Token),
            await PageTools.PageGetAsync(_client, "=A/1", null, ToolArguments.Concise, null, cancellationToken: Token),
        ];

        Assert.All(results, r => Assert.Equal(ErrorReason.ProjectNotOpen, Error(r).GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task TwoProjectsWithOneName_IsAmbiguousWithBothPathsInTheMetadata()
    {
        _catalog.Projects = [_line1, new OpenProject { Name = "Line1", Path = @"E:\存档\Line1" }];

        JsonElement error = Error(await PageTools.PageListAsync(_client, "Line1", null, null, null, null, 50, null, Token));

        Assert.Equal(ErrorReason.AmbiguousName, error.GetProperty("reason").GetString());
        Assert.Equal([@"D:\Projects\Line1", @"E:\存档\Line1"], error.GetProperty("metadata").GetProperty("candidates").GetString()!.Split('\n'));
    }

    [Fact]
    public async Task PageList_FiltersCombineAndTheCursorWalksTheMatches()
    {
        _catalog.Projects = [_line1];
        var names = new List<string>();
        string? cursor = null;
        do
        {
            JsonElement page = Structured(await PageTools.PageListAsync(_client, null, "=GAA", "CIRCUIT", "power", null, 1, cursor, Token));
            names.AddRange(page.GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("name").GetString()!));
            Assert.Equal(2, page.GetProperty("total").GetInt32());
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(["=GAA+A1&EFS1/1", "=GAA+A2&EFS1/1"], names);
    }

    [Fact]
    public async Task PageList_UnknownPageType_IsInvalidArgumentFromTheBridge()
    {
        _catalog.Projects = [_line1];

        JsonElement error = Error(await PageTools.PageListAsync(_client, null, null, "Schematic", null, null, 50, null, Token));

        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Contains("TerminalDiagram", error.GetProperty("metadata").GetProperty("violations").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PageGet_FindsThePageInAnyCaseAndListsItsDevices()
    {
        _catalog.Projects = [_line1];

        CallToolResult result = await PageTools.PageGetAsync(_client, "=gaa+a1&efs1/1", null, ToolArguments.Concise, null, cancellationToken: Token);

        JsonElement data = Structured(result);
        Assert.Equal("=GAA+A1&EFS1/1", data.GetProperty("page").GetProperty("name").GetString());
        Assert.Equal("电源 24 V", data.GetProperty("page").GetProperty("description").GetString());
        Assert.Equal(["=GAA+A1-FC1"], data.GetProperty("devices").EnumerateArray().Select(d => d.GetProperty("tag").GetString()));
        Assert.Equal("Page found: Circuit, 1 device.", ((TextContentBlock)result.Content[0]).Text);
    }

    [Fact]
    public async Task PageGet_UnknownPage_IsPageNotFoundWithThePageInTheMetadata()
    {
        _catalog.Projects = [_line1];

        JsonElement error = Error(await PageTools.PageGetAsync(_client, "=GAA+A9&EFS1/1", null, ToolArguments.Concise, null, cancellationToken: Token));

        Assert.Equal(ErrorReason.PageNotFound, error.GetProperty("reason").GetString());
        Assert.Equal("=GAA+A9&EFS1/1", error.GetProperty("metadata").GetProperty("page").GetString());
    }

    [Fact]
    public async Task Drawings_ThroughPipe_RejectsChangedEvidenceAndOtherHostCursors()
    {
        _catalog.Projects = [_line1];
        async Task<CallToolResult> Read(BridgeClient client, string? cursor = null) => await PageTools.PageGetAsync(client,
            "=gaa+a1&efs1/1", include_drawings: true, drawing_limit: 1, drawing_cursor: cursor, cancellationToken: Token);
        JsonElement first = Structured(await Read(_client));
        JsonElement drawings = first.GetProperty("drawings");
        Assert.Equal("Page.Size", drawings.GetProperty("boundarySource").GetString());
        string cursor = drawings.GetProperty("nextCursor").GetString()!;
        Assert.Equal(2, Structured(await Read(_client, cursor)).GetProperty("drawings").GetProperty("items")[0].GetProperty("locator").GetProperty("oid").GetInt64());
        _drawings.Geometry.Placements[0].Locator.Digest = new string('b', 64);
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Read(_client, cursor)).GetProperty("reason").GetString());
        _drawings.Geometry.Placements[0].Locator.Digest = new string('a', 64);
        await using var other = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        Assert.Equal(ErrorReason.InvalidArgument, Error(await Read(other, cursor)).GetProperty("reason").GetString());
    }

    private sealed class DrawingReader : IPageDrawingReader
    {
        public PageDrawingGeometry Geometry { get; } = new()
        {
            Page = "=GAA+A1&EFS1/1",
            GridSize = 4,
            PageSize = [420, 297],
            Placements = [.. Enumerable.Range(1, 2).Select(i => new DrawingPlacement
            {
                Kind = "Line", Location = [0, 0], Locator = new() { Project = _line1.Path, Page = "=GAA+A1&EFS1/1", Type = 4, Oid = i, Digest = new string('a', 64) },
            })],
        };
        public PageDrawingGeometry? Read(string projectPath, string pageName)
        {
            Assert.Equal((_line1.Path, Geometry.Page), (projectPath, pageName));
            return Geometry;
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    private static JsonElement Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
    }

    private sealed class Catalog : IProjectCatalog
    {
        public OpenProject[] Projects { get; set; } = [];

        public IReadOnlyList<OpenProject> ListOpenProjects() => Projects;
    }

    private sealed class ProjectReader : IProjectReader
    {
        public ProjectContent Read(string projectPath, bool includeProperties, string? language) => new()
        {
            PageTypes = ["TitlePage", "Circuit", "Circuit", "TerminalDiagram"],
            Devices = 1,
            Properties = includeProperties
                ? [new PropertyEntry { Id = 10000, Value = "Line1" }, new PropertyEntry { Id = 10901, Index = 2, Value = "附加字段 二" }]
                : null,
        };
    }

    private sealed class PageReader : IPageReader
    {
        private readonly PageSummary[] _pages =
        [
            new() { Name = "&AAA1/1", Type = "TitlePage" },
            new() { Name = "=GAA+A1&EFS1/1", Type = "Circuit", Description = "电源 24 V / Power supply" },
            new() { Name = "=GAA+A1&EFS1/2", Type = "Circuit", Description = "Motor" },
            new() { Name = "=GAA+A2&EFS1/1", Type = "Circuit", Description = "POWER distribution" },
        ];

        public IReadOnlyCollection<string> PageTypes { get; } = ["Circuit", "TerminalDiagram", "TitlePage"];

        public IReadOnlyList<PageSummary> ListPages(string projectPath, string? namePrefix, string? pageType, string? language) =>
            [.. _pages.Where(p => (namePrefix is null || p.Name.StartsWith(namePrefix, StringComparison.Ordinal)) && (pageType is null || p.Type == pageType))];

        public IReadOnlyList<string> PageNames(string projectPath) => [.. _pages.Select(p => p.Name)];

        public PageContent? ReadPage(string projectPath, string pageName, bool includeProperties, string? language) =>
            _pages.FirstOrDefault(p => p.Name == pageName) is { } page
                ? new PageContent
                {
                    Page = new PageDetail { Name = page.Name, Type = page.Type, Description = page.Description?.Split(" / ")[0] },
                    Functions = [new FunctionRecord { Name = "=GAA+A1-FC1", VisibleName = "-FC1", IsMain = true, Category = "CircuitBreaker" }],
                }
                : null;
    }

    private sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public DispatchResult Run(Func<object?> work, TimeSpan startTimeout)
        {
            try
            {
                return DispatchResult.Completed(work());
            }
            catch (Exception ex)
            {
                return DispatchResult.Faulted(ex);
            }
        }
    }

    private sealed class NullLog : IBridgeLog
    {
        public void Write(BridgeLogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
