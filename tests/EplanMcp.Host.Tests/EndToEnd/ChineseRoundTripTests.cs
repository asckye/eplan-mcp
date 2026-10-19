using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.EndToEnd;

/// <summary>
/// Chinese text through the whole chain in both directions: MCP client, stdio, <c>eplanmcp serve</c>, named pipe,
/// Bridge.Core server with fake EPLAN adapters, and back. Project names, page descriptions and property values are
/// often Chinese; a stream that used the console code page instead of UTF-8 would turn them into '?'. Run on a Chinese
/// Windows (code page 936), it also shows that nothing on the way depends on the code page; CI runs on an English one.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ChineseRoundTripTests : IAsyncLifetime
{
    private const string ProjectName = "项目一";
    private const string ProjectPath = @"D:\工程\项目一";

    // A CJK extension B character (a UTF-16 surrogate pair) and an ideographic space, beside common characters.
    private const string PropertyValue = "主电源进线　𠮷";

    private readonly string _prefix = "EplanMcpZh." + Guid.NewGuid().ToString("N") + ".";
    private readonly ProjectReader _reader = new();
    private BridgeServer? _bridge;
    private McpClient? _client;

    public async ValueTask InitializeAsync()
    {
        var router = new OperationRouter(
            [new ProjectGetOperation(new Catalog(), _reader)], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _bridge = new BridgeServer(
            _prefix + Environment.ProcessId + ".000000000000002a",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "zh", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _bridge.Start();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "eplanmcp",
            Command = Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"),
            Arguments = ["serve"],
            EnvironmentVariables = new Dictionary<string, string?> { [PipeBridgeDiscovery.PrefixVariable] = _prefix },
        });
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        _bridge?.Dispose();
    }

    [Fact]
    public async Task ChineseProjectName_SelectsTheProject_AndChineseResultsComeBackUnchanged()
    {
        // "Line1" is the current project: only a project name that reached the Bridge intact selects the other one.
        CallToolResult result = await Call(new() { ["project"] = ProjectName, ["response_format"] = "detailed", ["language"] = "zh_CN" });

        Assert.True(result.IsError is not true, ((TextContentBlock)result.Content[0]).Text);
        Assert.Equal(ProjectPath, _reader.ReadPath);
        JsonElement data = result.StructuredContent!.Value;
        Assert.Equal(ProjectName, data.GetProperty("project").GetProperty("name").GetString());
        Assert.Equal(ProjectPath, data.GetProperty("project").GetProperty("path").GetString());
        JsonElement property = Assert.Single(data.GetProperty("properties").EnumerateArray());
        Assert.Equal("项目描述", property.GetProperty("name").GetString());
        Assert.Equal(PropertyValue, property.GetProperty("value").GetString());

        // The JSON text block, for clients that show text only, carries the same value, with ideographs unescaped.
        // System.Text.Json still escapes U+3000 and surrogate pairs there (　, 𠮷); parsing restores them.
        string json = ((TextContentBlock)result.Content[1]).Text;
        Assert.Contains("\"项目描述\"", json, StringComparison.Ordinal);
        using JsonDocument text = JsonDocument.Parse(json);
        Assert.Equal(PropertyValue, text.RootElement.GetProperty("properties")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task ChineseNameOfAMissingProject_ComesBackUnchangedInTheError()
    {
        CallToolResult result = await Call(new() { ["project"] = "项目二" });

        Assert.True(result.IsError);
        using JsonDocument body = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text);
        JsonElement error = body.RootElement.GetProperty("error");
        Assert.Equal(ErrorReason.ProjectNotOpen, error.GetProperty("reason").GetString());

        // The Bridge echoes the name as it received it.
        Assert.Equal("项目二", error.GetProperty("metadata").GetProperty("project").GetString());
    }

    private Task<CallToolResult> Call(Dictionary<string, object?> args) =>
        _client!.CallToolAsync("eplan_project_get", args, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() =>
        [
            new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true },
            new OpenProject { Name = ProjectName, Path = ProjectPath },
        ];
    }

    private sealed class ProjectReader : IProjectReader
    {
        public string? ReadPath { get; private set; }

        public ProjectContent Read(string projectPath, bool includeProperties, string? language)
        {
            ReadPath = projectPath;
            return new ProjectContent
            {
                PageTypes = ["Circuit"],
                Properties = includeProperties ? [new PropertyEntry { Name = "项目描述", Value = PropertyValue }] : null,
            };
        }
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
