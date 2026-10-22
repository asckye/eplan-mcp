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
/// The database exports through the real ExportRunner, BridgeClient, pipe and Bridge.Core operations; only the EPLAN
/// adapter is fake, a parts database with two part numbers. The folder the tool reports is the one the Bridge wrote into.
/// </summary>
[Trait("Size", "Medium")]
public sealed class MasterDataExportInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly HostDataLocations _locations = new(Directory.CreateTempSubdirectory("eplanmcp-database-export-interop-").FullName);
    private readonly WritingExporter _exporter = new();
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;
    private readonly ExportRunner _runner;

    public MasterDataExportInteropTests()
    {
        IOperation[] operations =
        [
            new MasterDataExportPlanOperation(MasterDataExportKind.Parts, _exporter),
            new MasterDataExportRunOperation(MasterDataExportKind.Parts, _exporter),
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
        _runner = new ExportRunner(_client, new ExportFolders(_locations, time), time, NullLogger<ExportRunner>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _server.Dispose();
        Directory.Delete(_locations.Root, recursive: true);
    }

    [Fact]
    public async Task Parts_WriteOneEdzFileIntoTheToolsFolder_CountingPartsAndVariants()
    {
        CallToolResult result = await MasterDataExportTools.ExportPartsAsync(_runner, new RecordedProgress(), "edz", ["PXC.3209510", " SIE.3RH2122-1HB40"], "relays", TestContext.Current.CancellationToken);

        ToolResultAssert.Succeeded(result);
        JsonElement data = result.StructuredContent!.Value;
        string directory = data.GetProperty("directory").GetString()!;
        Assert.StartsWith(Path.Combine(_locations.Exports, "parts-database") + Path.DirectorySeparatorChar, directory, StringComparison.Ordinal);
        Assert.Equal(("SQL01|Parts", 2, 3, "relays.edz"), (data.GetProperty("database").GetString(), data.GetProperty("partCount").GetInt32(), data.GetProperty("variantCount").GetInt32(), data.GetProperty("files")[0].GetProperty("path").GetString()));
        Assert.Equal(["Edz PXC.3209510,SIE.3RH2122-1HB40 " + Path.Combine(directory, "relays.edz")], _exporter.Exports);
    }

    [Fact]
    public async Task Parts_AMissingPartNumber_IsPartNotFound_AndNoFolderIsCreated()
    {
        CallToolResult result = await MasterDataExportTools.ExportPartsAsync(_runner, new RecordedProgress(), "xml", ["SIE.3RH2122-1HB40", "SIE.NONE"], null, TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        JsonElement error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.GetProperty("error");
        Assert.Equal((ErrorReason.PartNotFound, "SIE.NONE"), (error.GetProperty("reason").GetString(), error.GetProperty("metadata").GetProperty("missing").GetString()));
        Assert.False(Directory.Exists(_locations.Exports));
        Assert.Empty(_exporter.Exports);
    }

    /// <summary>A parts database on SQL Server whose login must never reach the tool; PXC.3209510 has two variants.</summary>
    private sealed class WritingExporter : IMasterDataExporter
    {
        private readonly Dictionary<string, int> _parts = new(StringComparer.Ordinal) { ["SIE.3RH2122-1HB40"] = 1, ["PXC.3209510"] = 2 };

        public List<string> Exports { get; } = [];

        public PartsExport ExportParts(IReadOnlyList<string>? partNumbers, MasterDataFormat format, string? file)
        {
            IReadOnlyList<string> numbers = partNumbers ?? [.. _parts.Keys];
            var found = new PartsExport { Database = "SQL01|Parts|2|user|secret", PartCount = numbers.Count(_parts.ContainsKey), VariantCount = numbers.Where(_parts.ContainsKey).Sum(p => _parts[p]) };
            found.Missing.AddRange(numbers.Where(p => !_parts.ContainsKey(p)));
            if (file is not null && found.Missing.Count == 0)
            {
                Exports.Add($"{format} {string.Join(",", numbers)} {file}");
                File.WriteAllText(file, "<partsmanagement/>");
            }

            return found;
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
