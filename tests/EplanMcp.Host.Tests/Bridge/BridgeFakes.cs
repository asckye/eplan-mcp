using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Bridge;

internal sealed class FakeDiscovery : IBridgeDiscovery
{
    public List<BridgeEndpoint> Endpoints { get; } = [];

    public Exception? Failure { get; set; }

    public IReadOnlyList<BridgeEndpoint> Discover() => Failure is null ? [.. Endpoints] : throw Failure;
}

/// <summary>A channel whose answers come from a delegate; answers handshake compatibly unless told otherwise.</summary>
internal sealed class FakeChannel(BridgeEndpoint endpoint, Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? invoke = null) : IBridgeChannel
{
    public BridgeEndpoint Endpoint => endpoint;

    public bool Disposed { get; private set; }

    public Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? Handshake { get; set; }

    public List<string> Methods { get; } = [];

    /// <summary>Where the call in progress sends the Bridge's progress reports; null outside a call or when none listens.</summary>
    public IProgress<ProgressNotification>? Progress { get; private set; }

    public Task<JsonNode?> CallAsync(string method, JsonNode? parameters, IProgress<ProgressNotification>? progress, CancellationToken cancellationToken)
    {
        Methods.Add(method);
        Progress = progress;
        if (method == BridgeProtocol.HandshakeMethod)
        {
            return Handshake?.Invoke(method, parameters, cancellationToken) ?? Task.FromResult(CompatibleHandshake(endpoint.ProcessId));
        }

        return invoke?.Invoke(method, parameters, cancellationToken) ?? Task.FromResult<JsonNode?>(new JsonObject { ["data"] = new JsonObject { ["projects"] = new JsonArray() } });
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Without <paramref name="operations"/> the answer is an M1 add-in's, which announces none.</summary>
    public static JsonNode? CompatibleHandshake(int processId, int protocolVersion = BridgeProtocol.CurrentVersion, List<string>? operations = null) =>
        JsonSerializer.SerializeToNode(
            new HandshakeResult
            {
                ProtocolVersion = protocolVersion,
                BridgeVersion = "b",
                Eplan = new EplanInstanceInfo { ProcessId = processId, Version = "2026.0.3" },
                Operations = operations,
            },
            HostJson.Options);

    public static BridgeRpcException BusinessError(string reason, string message = "m", bool retryable = false) =>
        new(BridgeProtocol.BusinessErrorCode, message, JsonSerializer.SerializeToNode(new BridgeError { Reason = reason, Message = message, Retryable = retryable }, HostJson.Options));
}

/// <summary>Keeps every report as it is made, on the reporting thread (unlike <see cref="Progress{T}"/>).</summary>
internal sealed class RecordedReports<T> : IProgress<T>
{
    private readonly Lock _lock = new();
    private readonly List<T> _values = [];

    public List<T> Values
    {
        get
        {
            lock (_lock)
            {
                return [.. _values];
            }
        }
    }

    public void Report(T value)
    {
        lock (_lock)
        {
            _values.Add(value);
        }
    }
}

internal sealed class FakeConnector(Func<BridgeEndpoint, FakeChannel> create) : IBridgeConnector
{
    public List<FakeChannel> Opened { get; } = [];

    public Exception? Failure { get; set; }

    public Task<IBridgeChannel> ConnectAsync(BridgeEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException<IBridgeChannel>(Failure);
        }

        FakeChannel channel = create(endpoint);
        Opened.Add(channel);
        return Task.FromResult<IBridgeChannel>(channel);
    }
}
