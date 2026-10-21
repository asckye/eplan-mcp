using System.Text.Json;
using System.Text.Json.Serialization;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// An enum as exactly its snake_case name. Unlike <see cref="JsonStringEnumConverter"/>, it reads no other case, no
/// number and no comma-joined list of names.
/// </summary>
internal sealed class StrictEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    private static readonly Dictionary<string, T> _values = Enum.GetValues<T>().ToDictionary(v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()), StringComparer.Ordinal);
    private static readonly Dictionary<T, string> _names = _values.ToDictionary(pair => pair.Value, pair => pair.Key);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && _values.TryGetValue(reader.GetString()!, out T value)
            ? value
            : throw new JsonException($"The value is not one of the names of {typeof(T).Name}.");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(_names[value]);
}
