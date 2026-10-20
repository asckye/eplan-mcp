using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Server;

/// <summary>
/// Serves one Host connection: reads frames, answers <c>handshake</c> and <c>invoke</c>, writes responses.
/// Requests on one connection are handled one at a time, in order. Owned by a single listener task.
/// </summary>
internal sealed class BridgeConnection
{
    private readonly BridgeServerOptions _options;
    private readonly OperationRouter _router;
    private readonly IBridgeLog _log;
    private bool _handshaken;

    public BridgeConnection(BridgeServerOptions options, OperationRouter router, IBridgeLog log)
    {
        _options = options;
        _router = router;
        _log = log;
    }

    /// <summary>Runs until the peer disconnects, the framing breaks or <paramref name="cancellationToken"/> fires.</summary>
    public async Task RunAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? frame;
            try
            {
                frame = await Framing.ReadFrameAsync(stream, BridgeProtocol.MaxFrameBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (ProtocolException ex)
            {
                _log.Write(BridgeLogLevel.Warning, "Closing a connection that broke the framing: " + ex.Message);
                return;
            }
            catch (IOException)
            {
                return; // the Host went away (pipe broken): a normal end of the connection
            }

            if (frame is null)
            {
                return;
            }

            JObject? response = Handle(frame);
            if (response is not null)
            {
                try
                {
                    await Framing.WriteFrameAsync(stream, BridgeJson.ToUtf8(response), cancellationToken).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    return; // the Host gave up waiting (timeout) or exited; nobody is left to answer
                }
            }
        }
    }

    /// <summary>Handles one frame; returns the response, or null for a notification. Never throws.</summary>
    internal JObject? Handle(byte[] frame)
    {
        JObject request;
        try
        {
            request = BridgeJson.Parse(frame) as JObject ?? throw new JsonReaderException("not an object");
        }
        catch (JsonException)
        {
            return Error(JValue.CreateNull(), RpcException.ParseError, "Parse error.", null);
        }

        JToken? id = request["id"];
        try
        {
            if ((string?)request["jsonrpc"] != "2.0" || request["method"]?.Type != JTokenType.String)
            {
                throw new RpcException(RpcException.InvalidRequest, "Invalid request.");
            }

            string method = (string)request["method"]!;
            JToken? parameters = request["params"];
            if (id is null)
            {
                // Notifications (cancel, reserved for M4) have no response.
                _log.Write(BridgeLogLevel.Debug, "Ignoring notification " + method);
                return null;
            }

            JToken result = method switch
            {
                BridgeProtocol.HandshakeMethod => Handshake(parameters),
                BridgeProtocol.InvokeMethod => Invoke(parameters),
                _ => throw new RpcException(RpcException.MethodNotFound, $"Method '{method}' not found."),
            };
            return new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
        }
        catch (RpcException ex)
        {
            return Error(id, ex.Code, ex.Message, null);
        }
        catch (BridgeErrorException ex)
        {
            return Error(id, BridgeProtocol.BusinessErrorCode, ex.Error.Message, BridgeJson.FromObject(ex.Error));
        }
        catch (Exception ex)
        {
            _log.Write(BridgeLogLevel.Error, "Unexpected failure while handling a request", ex);
            return Error(id, RpcException.InternalError, "Internal error.", null);
        }
    }

    private JToken Handshake(JToken? parameters)
    {
        _handshaken = false; // a new handshake replaces the old one, even if it fails
        HandshakeRequest request;
        try
        {
            request = parameters?.Type == JTokenType.Object
                ? parameters.ToObject<HandshakeRequest>(BridgeJson.Serializer) ?? new HandshakeRequest()
                : throw new RpcException(RpcException.InvalidParams, "handshake needs an object parameter.");
        }
        catch (JsonException)
        {
            throw new RpcException(RpcException.InvalidParams, "handshake parameters are invalid.");
        }

        if (BridgeProtocol.CurrentVersion < request.MinProtocolVersion || BridgeProtocol.CurrentVersion > request.ProtocolVersion)
        {
            _log.Write(BridgeLogLevel.Warning, $"Incompatible Host {request.HostVersion} (protocol {request.MinProtocolVersion}-{request.ProtocolVersion})");
            throw new BridgeErrorException(
                ErrorReason.BridgeIncompatible,
                $"The EplanMcp add-in speaks protocol {BridgeProtocol.CurrentVersion}, but this Host accepts {request.MinProtocolVersion}-{request.ProtocolVersion}. Run 'eplanmcp install-addin' and restart EPLAN.");
        }

        _handshaken = true;
        _log.Write(BridgeLogLevel.Information, $"Handshake with Host {request.HostVersion}");
        return BridgeJson.FromObject(new HandshakeResult
        {
            ProtocolVersion = BridgeProtocol.CurrentVersion,
            BridgeVersion = _options.BridgeVersion,
            Eplan = _options.Eplan,
            Operations = [.. _router.SupportedOperations],
            BuildTarget = _options.BuildTarget,
        });
    }

    private JObject Invoke(JToken? parameters)
    {
        if (!_handshaken)
        {
            throw new RpcException(BridgeProtocol.NotHandshakenErrorCode, "handshake must come first.");
        }

        if (parameters is not JObject envelope || envelope["operation"]?.Type != JTokenType.String)
        {
            throw new RpcException(RpcException.InvalidParams, "invoke needs {operation, correlationId, args}.");
        }

        string correlationId = envelope["correlationId"] switch
        {
            null or { Type: JTokenType.Null } => Guid.NewGuid().ToString("N"),
            { Type: JTokenType.String } id => (string)id!,
            _ => throw new RpcException(RpcException.InvalidParams, "invoke correlationId must be a string."),
        };
        JToken data = _router.Invoke((string)envelope["operation"]!, envelope["args"], correlationId);
        return new JObject { ["data"] = data };
    }

    private static JObject Error(JToken? id, int code, string message, JToken? data)
    {
        var error = new JObject { ["code"] = code, ["message"] = message };
        if (data is not null)
        {
            error["data"] = data;
        }

        return new JObject { ["jsonrpc"] = "2.0", ["id"] = id ?? JValue.CreateNull(), ["error"] = error };
    }
}
