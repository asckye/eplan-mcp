using System.IO.Pipes;
using System.Security.Cryptography;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Server;

/// <summary>
/// Listens on the Bridge's named pipe with <see cref="BridgeServerOptions.MaxConnections"/> listener tasks, each
/// serving one Host connection at a time. Creating the pipe stream is delegated to the host-specific layer because
/// access control differs between net481 (PipeSecurity) and .NET 10 (PipeOptions.CurrentUserOnly); the factory
/// must create the stream with <see cref="PipeOptions.Asynchronous"/>.
/// <para>
/// <see cref="Dispose"/> never waits for the listeners: it may be called on EPLAN's main thread (OnExit) while a
/// listener is blocked waiting for that same thread.
/// </para>
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private readonly Func<string, NamedPipeServerStream> _createPipe;
    private readonly BridgeServerOptions _options;
    private readonly OperationRouter _router;
    private readonly IBridgeLog _log;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly HashSet<NamedPipeServerStream> _pipes = [];
    private bool _started;

    public BridgeServer(string pipeName, Func<string, NamedPipeServerStream> createPipe, BridgeServerOptions options, OperationRouter router, IBridgeLog log)
    {
        PipeName = pipeName;
        _createPipe = createPipe;
        _options = options;
        _router = router;
        _log = log;
    }

    public string PipeName { get; }

    /// <summary><c>EplanMcp.Bridge.&lt;pid&gt;.&lt;16 hex digits&gt;</c>; the random part prevents name squatting (SEC-03).</summary>
    public static string CreatePipeName(int processId)
    {
        byte[] random = new byte[8];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(random);
        }

        return BridgeProtocol.PipePrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
            BitConverter.ToString(random).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// Creates the first pipe instance before returning, so a Host can discover the pipe as soon as this returns,
    /// and a failure to create it (access denied, for example) reaches the caller instead of only the log.
    /// </summary>
    public void Start()
    {
        NamedPipeServerStream first;
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            first = _createPipe(PipeName);
            _pipes.Add(first);
            _started = true;
        }

        _ = Task.Run(() => ListenAsync(first));
        for (int i = 1; i < Math.Max(1, _options.MaxConnections); i++)
        {
            _ = Task.Run(() => ListenAsync(null));
        }

        _log.Write(BridgeLogLevel.Information, $"Listening on pipe {PipeName}");
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();
        lock (_gate)
        {
            foreach (NamedPipeServerStream pipe in _pipes)
            {
                pipe.Dispose();
            }

            _pipes.Clear();
        }

        _log.Write(BridgeLogLevel.Information, "Pipe server stopped");
    }

    private async Task ListenAsync(NamedPipeServerStream? first)
    {
        CancellationToken stop = _stop.Token;
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = first ?? _createPipe(PipeName);
                first = null;
                lock (_gate)
                {
                    if (stop.IsCancellationRequested)
                    {
                        pipe.Dispose();
                        return;
                    }

                    _pipes.Add(pipe);
                }

                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                await new BridgeConnection(_options, _router, _log).RunAsync(pipe, stop).ConfigureAwait(false);
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A broken connection must not take the listener (or EPLAN) down; back off and listen again.
                _log.Write(BridgeLogLevel.Warning, "Pipe listener error", ex);
                await DelayQuietlyAsync(stop).ConfigureAwait(false);
            }
            finally
            {
                if (pipe is not null)
                {
                    lock (_gate)
                    {
                        _pipes.Remove(pipe);
                    }

                    pipe.Dispose();
                }
            }
        }
    }

    private static async Task DelayQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }
}
