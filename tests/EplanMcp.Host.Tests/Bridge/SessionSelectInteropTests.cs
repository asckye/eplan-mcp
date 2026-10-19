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
/// Two Bridges on real pipes, standing in for an Electric P8 and a Pro Panel EPLAN with different projects open,
/// reached through the real BridgeClient and tools. Both run in this test process, so a fixed discovery gives them
/// distinct process IDs (as the pipe names of two EPLAN processes would).
/// </summary>
[Trait("Size", "Medium")]
public sealed class SessionSelectInteropTests : IAsyncDisposable
{
    private const int ElectricP8 = 24688;
    private const int ProPanel = 31024;

    private readonly BridgeServer _electricP8;
    private readonly BridgeServer _proPanel;
    private readonly BridgeClient _client;

    public SessionSelectInteropTests()
    {
        _electricP8 = Start(ElectricP8, "Electric P8", "Line1");
        _proPanel = Start(ProPanel, "Pro Panel", "Cabinet7");
        var discovery = new FixedDiscovery([new BridgeEndpoint(_electricP8.PipeName, ElectricP8), new BridgeEndpoint(_proPanel.PipeName, ProPanel)]);
        _client = new BridgeClient(discovery, new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _electricP8.Dispose();
        _proPanel.Dispose();
    }

    [Fact]
    public async Task WithoutASelection_ToolsAskToSelectAndStatusListsBoth()
    {
        CallToolResult list = await ProjectList();
        JsonElement status = (await Status()).StructuredContent!.Value;

        Assert.True(list.IsError);
        Assert.Contains(ErrorReason.SessionNotSelected, Text(list), StringComparison.Ordinal);
        Assert.False(status.GetProperty("connected").GetBoolean());
        Assert.Equal([ElectricP8, ProPanel], status.GetProperty("instances").EnumerateArray().Select(i => i.GetProperty("processId").GetInt32()));
    }

    [Fact]
    public async Task Select_ThenEveryToolWorksWithTheChosenEplanUntilAnotherIsChosen()
    {
        CallToolResult selected = await Select(ProPanel);
        string firstProject = Projects(await ProjectList()).Single();
        JsonElement status = (await Status()).StructuredContent!.Value;

        await Select(ElectricP8);
        string secondProject = Projects(await ProjectList()).Single();

        Assert.Equal($"Selected EPLAN 2026.0.3.25421 (Pro Panel), process {ProPanel}.", Text(selected));
        Assert.Equal(("Cabinet7", "Line1"), (firstProject, secondProject));
        Assert.Equal("Pro Panel", status.GetProperty("eplan").GetProperty("variant").GetString());
        Assert.Equal([false, true], status.GetProperty("instances").EnumerateArray().Select(i => i.GetProperty("selected").GetBoolean()));
    }

    [Fact]
    public async Task Select_AProcessWithoutTheAddIn_ListsTheCandidates()
    {
        CallToolResult result = await Select(4242);

        Assert.True(result.IsError);
        JsonElement error = JsonDocument.Parse(Text(result)).RootElement.GetProperty("error");
        Assert.Equal(ErrorReason.InvalidArgument, error.GetProperty("reason").GetString());
        Assert.Equal($"{ElectricP8},{ProPanel}", error.GetProperty("metadata").GetProperty("processIds").GetString());
    }

    private static BridgeServer Start(int processId, string variant, string project)
    {
        var catalog = new Catalog(project);
        var router = new OperationRouter([new ProjectListOperation(catalog)], new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        var server = new BridgeServer(
            "EplanMcpTest." + Guid.NewGuid().ToString("N") + "." + processId + ".0000000000000005",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "select", Eplan = new EplanInstanceInfo { ProcessId = processId, Version = "2026.0.3.25421", Variant = variant } },
            router,
            new NullLog());
        server.Start();
        return server;
    }

    private Task<CallToolResult> Select(int processId) => SessionTools.SessionSelectAsync(_client, processId, TestContext.Current.CancellationToken);

    private Task<CallToolResult> Status() => SessionTools.SessionStatusAsync(_client, TestContext.Current.CancellationToken);

    private Task<CallToolResult> ProjectList() => ProjectTools.ProjectListAsync(_client, cancellationToken: TestContext.Current.CancellationToken);

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static IEnumerable<string> Projects(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("name").GetString()!);
    }

    private sealed class FixedDiscovery(IReadOnlyList<BridgeEndpoint> endpoints) : IBridgeDiscovery
    {
        public IReadOnlyList<BridgeEndpoint> Discover() => endpoints;
    }

    private sealed class Catalog(string project) : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = project, Path = $@"D:\Projects\{project}", Current = true }];
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
