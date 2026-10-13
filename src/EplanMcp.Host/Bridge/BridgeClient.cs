using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Bridge;

/// <summary>
/// Connects lazily, on the first call that needs EPLAN, and keeps one channel to the single discovered Bridge.
/// Discovery runs before every call, so a restarted EPLAN (new pipe name) is picked up without a failed call.
/// <para>
/// Ownership: <see cref="_queue"/> guards <see cref="_channel"/> and <see cref="_handshake"/>; every call holds it
/// for its whole duration, which also makes the queue per EPLAN instance serial (architecture 8.3). A cancelled or
/// timed-out call drops the channel, because a late response would otherwise be read by the next call.
/// </para>
/// </summary>
internal sealed partial class BridgeClient : IBridgeClient, IAsyncDisposable
{
    public static readonly TimeSpan DefaultQueueTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan DefaultCallTimeout = TimeSpan.FromSeconds(30);

    private readonly IBridgeDiscovery _discovery;
    private readonly IBridgeConnector _connector;
    private readonly ILogger<BridgeClient> _logger;
    private readonly TimeSpan _queueTimeout;
    private readonly TimeSpan _callTimeout;
    private readonly SemaphoreSlim _queue = new(1, 1);
    private IBridgeChannel? _channel;
    private HandshakeResult? _handshake;

    public BridgeClient(IBridgeDiscovery discovery, IBridgeConnector connector, ILogger<BridgeClient> logger)
        : this(discovery, connector, logger, DefaultQueueTimeout, DefaultCallTimeout)
    {
    }

    internal BridgeClient(IBridgeDiscovery discovery, IBridgeConnector connector, ILogger<BridgeClient> logger, TimeSpan queueTimeout, TimeSpan callTimeout)
    {
        _discovery = discovery;
        _connector = connector;
        _logger = logger;
        _queueTimeout = queueTimeout;
        _callTimeout = callTimeout;
    }

    public async Task<BridgeStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BridgeEndpoint> instances = Discover();
        try
        {
            HandshakeResult handshake = await RunExclusiveAsync(token => EnsureConnectedAsync(instances, token), cancellationToken).ConfigureAwait(false);
            return new BridgeStatus(instances, handshake, null);
        }
        catch (BridgeCallException ex)
        {
            return new BridgeStatus(instances, null, ex.Error);
        }
    }

    public Task<TData> InvokeAsync<TArgs, TData>(string operation, TArgs args, CancellationToken cancellationToken)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        return RunExclusiveAsync(
            async token =>
            {
                await EnsureConnectedAsync(Discover(), token).ConfigureAwait(false);
                var parameters = new JsonObject
                {
                    ["operation"] = operation,
                    ["correlationId"] = correlationId,
                    ["args"] = JsonSerializer.SerializeToNode(args, HostJson.Options),
                };
                LogInvoke(operation, correlationId);
                JsonNode? result = await CallAsync(BridgeProtocol.InvokeMethod, parameters, token).ConfigureAwait(false);
                try
                {
                    return result?["data"] is { } data && data.Deserialize<TData>(HostJson.Options) is { } typed
                        ? typed
                        : throw new JsonException("The invoke result has no data.");
                }
                catch (JsonException ex)
                {
                    LogUnexpected(ex, correlationId);
                    throw new BridgeCallException(BridgeErrors.Internal(correlationId));
                }
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync().ConfigureAwait(false);
        _queue.Dispose();
    }

    private async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken cancellationToken)
    {
        if (!await _queue.WaitAsync(_queueTimeout, cancellationToken).ConfigureAwait(false))
        {
            throw new BridgeCallException(BridgeErrors.QueueBusy());
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_callTimeout);
            try
            {
                return await body(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await DropAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new BridgeCallException(BridgeErrors.Timeout(_callTimeout));
            }
        }
        finally
        {
            _queue.Release();
        }
    }

    private async Task<HandshakeResult> EnsureConnectedAsync(IReadOnlyList<BridgeEndpoint> instances, CancellationToken cancellationToken)
    {
        if (instances.Count == 0)
        {
            await DropAsync().ConfigureAwait(false);
            throw new BridgeCallException(BridgeErrors.NotConnected());
        }

        if (instances.Count > 1)
        {
            throw new BridgeCallException(BridgeErrors.SessionNotSelected(instances));
        }

        BridgeEndpoint target = instances[0];
        if (_channel is not null && _handshake is not null && _channel.Endpoint == target)
        {
            return _handshake;
        }

        await DropAsync().ConfigureAwait(false);
        IBridgeChannel? pending;
        try
        {
            pending = await _connector.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            LogConnectFailed(ex, target.PipeName);
            throw new BridgeCallException(BridgeErrors.NotConnected());
        }

        try
        {
            var request = new HandshakeRequest
            {
                ProtocolVersion = BridgeProtocol.CurrentVersion,
                MinProtocolVersion = BridgeProtocol.MinimumSupportedVersion,
                HostVersion = HostInfo.Version,
            };
            JsonNode? result = await pending.CallAsync(BridgeProtocol.HandshakeMethod, JsonSerializer.SerializeToNode(request, HostJson.Options), cancellationToken).ConfigureAwait(false);
            HandshakeResult handshake = result.Deserialize<HandshakeResult>(HostJson.Options) ?? throw new JsonException("empty handshake result");
            if (handshake.ProtocolVersion is < BridgeProtocol.MinimumSupportedVersion or > BridgeProtocol.CurrentVersion)
            {
                throw new BridgeCallException(BridgeErrors.Incompatible(handshake.ProtocolVersion));
            }

            LogConnected(target.ProcessId, handshake.Eplan.Version, handshake.BridgeVersion);
            (_channel, _handshake) = (pending, handshake);
            pending = null; // owned by _channel now
            return handshake;
        }
        catch (BridgeRpcException ex)
        {
            throw Map(ex, "handshake");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            LogConnectFailed(ex, target.PipeName);
            throw new BridgeCallException(BridgeErrors.NotConnected());
        }
        finally
        {
            if (pending is not null)
            {
                await pending.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<JsonNode?> CallAsync(string method, JsonNode parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await _channel!.CallAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeRpcException ex)
        {
            throw Map(ex, method); // the channel stays usable after an error response
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            LogConnectionLost(ex);
            await DropAsync().ConfigureAwait(false);
            throw new BridgeCallException(BridgeErrors.ConnectionLost());
        }
    }

    private BridgeCallException Map(BridgeRpcException ex, string method)
    {
        if (ex.Code == BridgeProtocol.BusinessErrorCode && ex.ErrorData is JsonObject data)
        {
            try
            {
                if (data.Deserialize<BridgeError>(HostJson.Options) is { Reason.Length: > 0 } error)
                {
                    return new BridgeCallException(error);
                }
            }
            catch (JsonException)
            {
                // fall through: a malformed error is our defect
            }
        }

        string correlationId = Guid.NewGuid().ToString("N");
        LogRejected(ex.Code, ex.Message, method, correlationId);
        return new BridgeCallException(BridgeErrors.Internal(correlationId));
    }

    /// <summary>Discovery failures (pipe listing, process lookup) mean "no usable EPLAN", never an escaped exception.</summary>
    private IReadOnlyList<BridgeEndpoint> Discover()
    {
        try
        {
            return _discovery.Discover();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogDiscoveryFailed(ex);
            return [];
        }
    }

    private async Task DropAsync()
    {
        IBridgeChannel? channel = _channel;
        (_channel, _handshake) = (null, null);
        if (channel is not null)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to EPLAN process {ProcessId} (EPLAN {EplanVersion}, add-in {BridgeVersion})")]
    private partial void LogConnected(int processId, string eplanVersion, string bridgeVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not connect to Bridge pipe {PipeName}")]
    private partial void LogConnectFailed(Exception exception, string pipeName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bridge discovery failed")]
    private partial void LogDiscoveryFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection to the Bridge was lost")]
    private partial void LogConnectionLost(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Invoking {Operation} ({CorrelationId})")]
    private partial void LogInvoke(string operation, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bridge rejected {Method} with JSON-RPC {Code}: {Message} ({CorrelationId})")]
    private partial void LogRejected(int code, string message, string method, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unreadable Bridge result ({CorrelationId})")]
    private partial void LogUnexpected(Exception exception, string correlationId);
}
