using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Typed base for operations whose arguments and result are contract DTOs.</summary>
public abstract class Operation<TArgs, TData> : IOperation
    where TArgs : class, new()
{
    public abstract string Name { get; }

    public virtual object ParseArgs(JToken? args)
    {
        if (args is null || args.Type == JTokenType.Null)
        {
            return new TArgs();
        }

        if (args.Type != JTokenType.Object)
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, $"Arguments of '{Name}' must be a JSON object.");
        }

        try
        {
            return args.ToObject<TArgs>(BridgeJson.Serializer) ?? new TArgs();
        }
        catch (JsonException ex)
        {
            // Newtonsoft's message names CLR types; only the JSON path goes back (coding standards 4.4).
            string path = ex is JsonSerializationException { Path: { } p } ? p : ex is JsonReaderException { Path: { } q } ? q : string.Empty;
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                $"Arguments of '{Name}' are invalid.",
                retryable: false,
                new Dictionary<string, string> { ["path"] = path });
        }
    }

    public object? Execute(object args) => Execute((TArgs)args);

    protected abstract TData Execute(TArgs args);

    /// <summary>A blank argument is no argument; models sometimes pass "" for one they mean to omit.</summary>
    protected static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    /// <summary>
    /// INVALID_ARGUMENT for one argument problem, in the shape the Host's ToolResults.InvalidArguments gives: the
    /// message and <c>metadata.violations</c> carry the same text.
    /// </summary>
    protected static BridgeErrorException InvalidArgument(string violation) => new(
        ErrorReason.InvalidArgument,
        "Invalid arguments: " + violation,
        retryable: false,
        new Dictionary<string, string> { ["violations"] = violation });
}
