using System.Collections.Concurrent;
using System.IO.Pipes;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// Discovery against the real pipe namespace while older pipes close, as on a busy machine or while other test
/// classes stop their Bridge servers. Listing the whole namespace skipped a Bridge in about one scan in ten here, which
/// the interop tests reported as an intermittent EPLAN_NOT_CONNECTED.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PipeBridgeDiscoveryInteropTests
{
    private readonly string _prefix = "EplanMcpTest." + Guid.NewGuid().ToString("N") + ".";

    [Fact]
    public async Task Discover_WhileOlderPipesClose_FindsEveryBridge()
    {
        var discovery = new PipeBridgeDiscovery(_prefix);
        string churnPrefix = "EplanMcpChurn." + Guid.NewGuid().ToString("N") + ".";
        int created = 150;
        var older = new ConcurrentQueue<NamedPipeServerStream>(Enumerable.Range(0, created).Select(i => Pipe(churnPrefix + i)));
        using var stop = new CancellationTokenSource();
        Task[] churn =
        [
            .. Enumerable.Range(0, 2).Select(_ => Task.Run(
                () =>
                {
                    // The oldest pipe closes; its replacement goes behind the Bridges of the current scan.
                    while (!stop.IsCancellationRequested)
                    {
                        if (older.TryDequeue(out NamedPipeServerStream? oldest))
                        {
                            oldest.Dispose();
                        }

                        older.Enqueue(Pipe(churnPrefix + Interlocked.Increment(ref created)));
                    }
                },
                TestContext.Current.CancellationToken)),
        ];

        var missed = new List<int>();
        try
        {
            for (int scan = 0; scan < 100; scan++)
            {
                string[] names = [$"{_prefix}{Environment.ProcessId}.{scan:x4}a", $"{_prefix}{Environment.ProcessId}.{scan:x4}b"];
                NamedPipeServerStream[] bridges = [.. names.Select(Pipe)];
                try
                {
                    if (!discovery.Discover().Select(e => e.PipeName).Order(StringComparer.Ordinal).SequenceEqual(names, StringComparer.Ordinal))
                    {
                        missed.Add(scan);
                    }
                }
                finally
                {
                    Array.ForEach(bridges, b => b.Dispose());
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(churn);
            while (older.TryDequeue(out NamedPipeServerStream? pipe))
            {
                pipe.Dispose();
            }
        }

        Assert.Empty(missed);
    }

    [Fact]
    public void Discover_NoPipeWithThePrefix_FindsNothing()
    {
        Assert.Empty(new PipeBridgeDiscovery(_prefix).Discover());
    }

    private static NamedPipeServerStream Pipe(string name) =>
        new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}
