using System.Text.Json;
using System.Text.Json.Serialization;

namespace EplanMcp.Workbench.Protocol;

/// <summary>A list of texts, such as a change list: an array of strings only, never a null among them.</summary>
internal sealed class TextListConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("The value is not an array of texts.");
        }

        var texts = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            texts.Add(reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException("An array of texts holds only strings."));
        }

        return texts;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string text in value)
        {
            writer.WriteStringValue(text);
        }

        writer.WriteEndArray();
    }
}
