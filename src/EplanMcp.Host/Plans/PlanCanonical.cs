using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EplanMcp.Host.Plans;

/// <summary>Design 4.1's byte-level format, independent of framework JSON escaping and property declaration order.</summary>
internal static class PlanCanonical
{
    private static readonly JsonSerializerOptions _json = new(PlanJson.Options)
    {
        Converters = { new TextConverter(), new TimeConverter(), new ElementConverter() },
    };

    public static string Hash(PlanDefinition definition) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(definition))));

    public static PlanDefinition ReadDefinition(string json)
    {
        PlanDefinition definition = PlanJson.Read<PlanDefinition>(json);
        _ = Serialize(definition);
        return definition;
    }

    public static string Serialize(PlanDefinition definition)
    {
        PlanDefinition normalized = definition with
        {
            RequiredOperations = Strings(definition.RequiredOperations),
            Sources = Objects(definition.Sources),
            Steps = [.. definition.Steps.Select(s => s with { ReadScope = Scope(s.ReadScope), ModifyScope = Scope(s.ModifyScope) })],
        };
        return Serialize(JsonSerializer.SerializeToElement(normalized, _json));
    }

    private static PlanScope Scope(PlanScope scope) => new(Strings(scope.Pages), Strings(scope.Structures), Objects(scope.Locators));
    private static ImmutableArray<string> Strings(ImmutableArray<string> values) => [.. values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    private static ImmutableArray<T> Objects<T>(ImmutableArray<T> values) =>
        [.. values.Select(v => (Value: v, Key: Serialize(JsonSerializer.SerializeToElement(v, _json))))
            .DistinctBy(v => v.Key, StringComparer.Ordinal).OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Value)];

    internal static string Serialize(JsonElement value)
    {
        var output = new StringBuilder();
        Append(value, output);
        return output.ToString();
    }

    private static void Append(JsonElement value, StringBuilder output)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                output.Append('{');
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty member in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    string key = member.Name.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
                    if (!names.Add(member.Name) || key.Equals("fingerprint", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("sessionFingerprint", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("ToStringIdentifier", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("DatabaseIdentifier", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new JsonException("Duplicate members and runtime identifiers are forbidden in plan definitions and evidence.");
                    }

                    if (names.Count > 1)
                    {
                        output.Append(',');
                    }

                    Quote(member.Name, output);
                    output.Append(':');
                    Append(member.Value, output);
                }

                output.Append('}');
                break;
            case JsonValueKind.Array:
                output.Append('[');
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        output.Append(',');
                    }

                    Append(item, output);
                }

                output.Append(']');
                break;
            case JsonValueKind.String:
                Quote(value.GetString()!, output);
                break;
            case JsonValueKind.Number:
                output.Append(Number(value.GetRawText()));
                break;
            case JsonValueKind.Null:
            case JsonValueKind.True:
            case JsonValueKind.False:
                output.Append(value.GetRawText());
                break;
            default:
                throw new JsonException("Undefined JSON has no canonical representation.");
        }
    }

    private static void Quote(string value, StringBuilder output)
    {
        output.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsSurrogate(c))
            {
                if (!char.IsHighSurrogate(c) || i + 1 == value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    throw new JsonException("Lone UTF-16 surrogates are forbidden.");
                }

                output.Append(c).Append(value[++i]);
                continue;
            }

            string? escape = c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\t' => "\\t",
                '\n' => "\\n",
                '\f' => "\\f",
                '\r' => "\\r",
                _ => null,
            };
            output.Append(escape ?? (c < ' ' ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : c.ToString()));
        }

        output.Append('"');
    }

    private static string Number(string raw)
    {
        // Normalize the exact decimal lexeme before parsing, so overflow, underflow and rounding cannot collapse hashes.
        string[] parts = raw.Split(['e', 'E']);
        string coefficient = parts[0];
        int point = coefficient.IndexOf('.', StringComparison.Ordinal);
        long scale = point < 0 ? 0 : coefficient.Length - point - 1;
        string digits = coefficient.Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('-').TrimStart('0');
        if (digits.Length == 0)
        {
            return "0";
        }

        int zeros = digits.Length - digits.TrimEnd('0').Length;
        digits = digits.TrimEnd('0');
        scale -= zeros;
        if (parts.Length > 1)
        {
            if (!long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long exponent)
                || exponent < scale - 28 || exponent > scale + 29 - digits.Length)
            {
                throw new JsonException("The number is outside the decimal range.");
            }

            scale -= exponent;
        }

        if (scale > 28 || digits.Length > 29 || digits.Length - scale > 29)
        {
            throw new JsonException("The number is outside the decimal range.");
        }

        int decimalPlaces = (int)scale;
        string fixedPoint = decimalPlaces <= 0 ? digits + new string('0', -decimalPlaces)
            : digits.PadLeft(decimalPlaces + 1, '0').Insert(Math.Max(1, digits.Length - decimalPlaces), ".");
        fixedPoint = (raw[0] == '-' ? "-" : string.Empty) + fixedPoint;
        if (!decimal.TryParse(fixedPoint, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)
            || number.ToString("0.############################", CultureInfo.InvariantCulture) != fixedPoint)
        {
            throw new JsonException("The number cannot be represented exactly as a decimal.");
        }

        return fixedPoint;
    }

    private sealed class TextConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetString();
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            var text = new StringBuilder();
            Quote(value, text);
            writer.WriteRawValue(text.ToString());
        }
    }

    private sealed class TimeConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTimeOffset();
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
    }

    private sealed class ElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonElement.ParseValue(ref reader);
        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) => writer.WriteRawValue(value.GetRawText());
    }
}
