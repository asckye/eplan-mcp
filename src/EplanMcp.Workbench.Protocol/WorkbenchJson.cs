using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// Writes and strictly reads the protocol's JSON-RPC 2.0 messages (ADR-0014 decision 3): camelCase names exactly,
/// enums as snake_case strings; an unknown, duplicate or missing field, a null where none is allowed, or a value of
/// another type makes a message invalid (<see cref="WorkbenchProtocolException"/>).
/// </summary>
public static class WorkbenchJson
{
    /// <summary>The strict settings, for both writing and reading.</summary>
    public static JsonSerializerOptions Options { get; } = Strict();

    /// <summary>A request with integer ID <paramref name="id"/>.</summary>
    public static byte[] Request<T>(long id, string method, T parameters) =>
        Encode(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = JsonSerializer.SerializeToNode(parameters, Options) });

    /// <summary>A notification: no ID, no answer.</summary>
    public static byte[] Notification<T>(string method, T parameters) =>
        Encode(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = JsonSerializer.SerializeToNode(parameters, Options) });

    /// <summary>The successful response to request <paramref name="id"/>.</summary>
    public static byte[] Result<T>(long id, T result) =>
        Encode(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = JsonSerializer.SerializeToNode(result, Options) });

    /// <summary>The error response to request <paramref name="id"/>; null when the request could not be read.</summary>
    public static byte[] Error(long? id, int code, string message) =>
        Encode(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

    /// <summary>One message as <see cref="RpcMessage"/>, checked to be a request, a notification or a response.</summary>
    public static RpcMessage Read(byte[] frame)
    {
        // The reader checks UTF-8 only in the strings it decodes, and params stay undecoded until they are used.
        if (!Utf8.IsValid(frame))
        {
            throw new WorkbenchProtocolException("The message is not valid UTF-8.");
        }

        RpcMessage message = Deserialize<RpcMessage>(frame);
        bool valid = message.Jsonrpc == "2.0" && (message.Method is null
            ? message.Params is null && (message.Result is null) != (message.Error is null) && (message.Id is not null || message.Error is not null)
            : message.Result is null && message.Error is null);
        return valid ? message : throw new WorkbenchProtocolException("The message is not a JSON-RPC 2.0 request, notification or response.");
    }

    /// <summary><paramref name="element"/> (a message's params or result) as <typeparamref name="T"/>; it must be present.</summary>
    public static T As<T>(JsonElement? element) =>
        element is { } value ? Convert<T>(() => value.Deserialize<T>(Options)) : throw new WorkbenchProtocolException("The message lacks its params or result.");

    private static T Deserialize<T>(byte[] utf8) => Convert<T>(() => JsonSerializer.Deserialize<T>(utf8, Options));

    private static T Convert<T>(Func<T?> read)
    {
        try
        {
            return read() ?? throw new WorkbenchProtocolException($"The message holds null where a {typeof(T).Name} belongs.");
        }
        catch (JsonException ex)
        {
            throw new WorkbenchProtocolException("The message is not valid: " + ex.Message, ex);
        }
    }

    private static byte[] Encode(JsonObject message) => JsonSerializer.SerializeToUtf8Bytes(message, Options);

    private static JsonSerializerOptions Strict()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            Converters =
            {
                new StrictEnumConverter<QuestionKind>(),
                new StrictEnumConverter<QuestionTier>(),
                new StrictEnumConverter<WorkbenchDecision>(),
                new StrictEnumConverter<WriteExecution>(),
                new TextListConverter(),
            },
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
