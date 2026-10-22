using System.IO.Pipes;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Bridge.Core.Threading;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// project.snapshot through the Host's BridgeClient, a real pipe and the Bridge.Core operation, with a fake reader: the
/// snapshot survives both serializers, and the Host recomputes the snapshot hash of a chunked snapshot from its pages
/// with the formula of docs/design/2026-10-21-m6-bridge-snapshot.md, 4.4.
/// </summary>
[Trait("Size", "Medium")]
public sealed class SnapshotInteropTests : IAsyncDisposable
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";
    private readonly BridgeServer _server;
    private readonly BridgeClient _client;

    public SnapshotInteropTests()
    {
        var reader = new Reader();
        var router = new OperationRouter(
            [new ProjectSnapshotOperation(new Catalog(), reader, reader, () => reader.Milliseconds)],
            new InlineDispatcher(),
            null,
            new NullLog(),
            TimeSpan.FromSeconds(10));
        _server = new BridgeServer(
            _prefix + Environment.ProcessId + ".00000000000000c3",
            name => new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly),
            new BridgeServerOptions { BridgeVersion = "snapshots", Eplan = new EplanInstanceInfo { ProcessId = Environment.ProcessId, Version = "2026.0.3.25421" } },
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
    public async Task Snapshot_InChunks_CarriesEveryFieldAndGivesTheHashOfTheWholeSnapshot()
    {
        ProjectSnapshotData whole = await SnapshotAsync(new ProjectSnapshotArgs { TimeLimitSeconds = 20 });
        ProjectSnapshotData first = await SnapshotAsync(new ProjectSnapshotArgs { TimeLimitSeconds = 1 });
        ProjectSnapshotData rest = await SnapshotAsync(new ProjectSnapshotArgs { TimeLimitSeconds = 20, AfterPage = first.NextAfterPage });

        Assert.Equal((2, true, 64), (whole.Pages.Count, whole.Complete, whole.SnapshotHash!.Length));
        Assert.Equal((1, false, 7L), (first.Pages.Count, first.Complete, first.NextAfterPage));
        Assert.Equal(whole.SnapshotHash, Rehash([.. first.Pages, .. rest.Pages]));
        SnapshotObject function = whole.Pages[0].Objects[1];
        Assert.Equal(("function", 17, 59641L, "=+-CU1", "120,180.5", "Coil", true), (function.Kind, function.Type, function.Oid, function.Name, function.Pos, function.Category, function.Main));
        SnapshotValue text = Assert.Single(function.Values);
        Assert.Equal(("FUNC_TEXT", "zh_CN", "电机"), (text.Name, text.Texts![1].Language, text.Texts[1].Text));
    }

    /// <summary>The snapshot hash as the Host computes it (design 4.4), written out here on purpose.</summary>
    private static string Rehash(IEnumerable<SnapshotPage> pages)
    {
        IEnumerable<string> lines = pages.Select(p => $"{p.Type}\t{p.Oid}\t{p.Hash}").Order(StringComparer.Ordinal);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", ["eplanmcp.snapshot/1 standard", .. lines])));
        return Convert.ToHexStringLower(hash);
    }

    private Task<ProjectSnapshotData> SnapshotAsync(ProjectSnapshotArgs args) =>
        _client.InvokeAsync<ProjectSnapshotArgs, ProjectSnapshotData>(OperationNames.ProjectSnapshot, args, TestContext.Current.CancellationToken);

    /// <summary>Two pages, 7 and 9, the first with the function =+-CU1; each page read takes 2 s.</summary>
    private sealed class Reader : ISnapshotReader, IProjectFiles
    {
        public long Milliseconds { get; private set; }

        public ISnapshotSource Open(string projectPath) => new Source(this, projectPath);

        public ISnapshotSource OpenReadOnly(string linkFile) => throw new InvalidOperationException("not expected");

        public (string Status, string DatabaseVersion) Compatibility(string linkFile) => ("IsCompatible", "18.0");

        private sealed class Source(Reader owner, string path) : ISnapshotSource
        {
            public string Name => "Line1";

            public string Path => path;

            public string? ProjectGuid => null;

            public string? ModifiedDate => null;

            public string? ModifiedTime => null;

            public IReadOnlyList<KeyValuePair<long, string>> Pages => [new(9, "=AP+ST1/2"), new(7, "=AP+ST1/1")];

            public int SkippedConnections => 0;

            public IReadOnlyList<SnapshotSourceObject> ReadPage(long pageOid, string profile)
            {
                owner.Milliseconds += 2000;
                var page = new SnapshotSourceObject { Kind = "page", Identifier = $"53/4/{pageOid}/0", Oid = pageOid, Name = pageOid == 7 ? "=AP+ST1/1" : "=AP+ST1/2", PageType = "Circuit" };
                return pageOid != 7
                    ? [page]
                    :
                    [
                        page,
                        new SnapshotSourceObject
                        {
                            Kind = "function",
                            Identifier = "53/17/59641/0",
                            Oid = 59641,
                            Name = "=+-CU1",
                            Page = "=AP+ST1/1",
                            Category = "Coil",
                            Main = true,
                            X = 120,
                            Y = 180.5,
                            Values = [new SnapshotSourceValue(20011, null, "zh_CN@电机;de_DE@Motor;")],
                        },
                    ];
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true }];
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
