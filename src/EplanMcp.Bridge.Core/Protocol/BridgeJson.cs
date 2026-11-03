using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Bridge.Core.Protocol;

/// <summary>
/// The Bridge's single JSON configuration: camelCase names (matching the Host's System.Text.Json web defaults,
/// guarded by the Contracts round-trip tests), no date guessing, bounded nesting.
/// </summary>
public static class BridgeJson
{
    public static JsonSerializer Serializer { get; } = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateParseHandling = DateParseHandling.None,
        MaxDepth = 64,
    });

    /// <summary>Parses a frame body; throws <see cref="JsonException"/> when it is not JSON.</summary>
    public static JToken Parse(byte[] utf8)
    {
        using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(utf8)))
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 64,
        };
        JToken token = JToken.ReadFrom(reader);
        return reader.Read() ? throw new JsonReaderException("Additional content after the JSON value.") : token;
    }

    internal static JObject ParseRequest(byte[] utf8)
    {
        JObject request = Parse(utf8) as JObject ?? throw new JsonReaderException("not an object");
        using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(utf8)))
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 64,
        };
        var members = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            // JObject replaces duplicates, so check the original wire names; params retain their own rules.
            if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1)
            {
                continue;
            }

            string name = (string)reader.Value!;
            if (name is not ("jsonrpc" or "id" or "method" or "params") || !members.Add(name))
            {
                throw new RpcException(RpcException.InvalidRequest, "Invalid request: duplicate or unknown envelope member.");
            }
        }

        return request;
    }

    public static byte[] ToUtf8(JToken token) => Encoding.UTF8.GetBytes(token.ToString(Formatting.None));

    public static JToken FromObject(object? value) => value is null ? JValue.CreateNull() : JToken.FromObject(value, Serializer);
}
