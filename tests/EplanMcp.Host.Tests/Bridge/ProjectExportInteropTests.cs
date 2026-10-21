using System.IO.Pipes;
using System.Text.Json;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Exports;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// The exports of a whole project through the real ExportRunner, BridgeClient, pipe and Bridge.Core operations; only
/// the EPLAN adapter is fake, writing what S25 found EPLAN writes (api-notes A25). The folder the tool reports is the
/// one the Bridge wrote into.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ProjectExportInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-project-export-interop-").FullName);
    private readonly WritingExporter _exporter = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;
    private readonly ExportRunner _runner;

    public ProjectExportInteropTests()
    {
        var catalog = new Catalog();
        IOperation[] operations =
        [
            .. new[] { ProjectExportKind.Pxf }.SelectMany(kind => new IOperation[] { new ProjectExportPlanOperation(kind, catalog), new ProjectExportRunOperation(kind, catalog, _exporter) }),
        ];
        var router = new OperationRouter(operations, new InlineDispatcher(), null, new NullLog(), TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000a1",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "exports", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
            router,
            new NullLog());
        _server.Start();
        _client = new BridgeClient(new PipeBridgeDiscovery(_prefix), new PipeBridgeConnector(), NullLogger<BridgeClient>.Instance);
        var time = new ManualTime(new DateTimeOffset(2026, 10, 21, 8, 30, 0, TimeSpan.Zero));
        _runner = new ExportRunner(_client, new ExportFolders(_locations, time) { FreeSpace = _ => long.MaxValue }, time, NullLogger<ExportRunner>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task Pxf_WritesOneEpjFileIntoTheToolsFolder_WithTheMasterDataAsked()
    {
        CallToolResult result = await ProjectExportTools.ExportPxfAsync(_runner, new RecordedProgress(), null, true, "Line1 archive.epj", TestContext.Current.CancellationToken);

        JsonElement data = Structured(result);
        string directory = data.GetProperty("directory").GetString()!;
        Assert.Equal(("Line 1", true, "Line1 archive.epj"), (data.GetProperty("project").GetString(), data.GetProperty("masterData").GetBoolean(), data.GetProperty("files")[0].GetProperty("path").GetString()));
        Assert.Equal([Path.Combine(directory, "Line1 archive") + " True"], _exporter.Exports);
        Assert.True(Directory.Exists(Path.Combine(directory, "Line1 archive")));
    }

    private static JsonElement Structured(CallToolResult result)
    {
        ToolResultAssert.Succeeded(result);
        return result.StructuredContent!.Value;
    }

    /// <summary>Writes as S25 found EPLAN writes: PXF adds .epj and leaves an empty folder of the file's name.</summary>
    private sealed class WritingExporter : IProjectExporter
    {
        public List<string> Exports { get; } = [];

        public void ExportPxf(string projectPath, string file, bool includeMasterData)
        {
            Exports.Add($"{file} {includeMasterData}");
            File.WriteAllText(file + ".epj", "pxf");
            Directory.CreateDirectory(file);
        }
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line 1", Path = @"D:\Projects\Line 1", Current = true }];
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
