using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Per-connection signing keys; only detached row fingerprints cross calls.</summary>
internal sealed class PropertyGetPaging(TimeProvider clock)
{
    private readonly ConditionalWeakTable<McpServer, PropertySchemaPaging> _sessions = new();

    internal PropertySchemaPaging For(McpServer server) => _sessions.GetValue(
        server.Services?.GetService(typeof(McpServer)) as McpServer ?? server, _ => new(clock));

    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string Canonical(JsonNode? node) => Normalize(node)?.ToJsonString(HostJson.Options) ?? "null";

    // Sorting object members makes whitespace/member order irrelevant; array positions remain part of the request.
    internal static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject([.. obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key switch
            {
                "project_object" => "projectObject",
                "device_tag" => "deviceTag",
                "property_id" => "propertyId",
                "property_name" => "propertyName",
                "projectObject" => "project_object",
                "deviceTag" => "device_tag",
                "propertyId" => "property_id",
                "propertyName" => "property_name",
                _ => p.Key,
            }, Normalize(p.Value)))]),
        JsonArray array => new JsonArray([.. array.Select(Normalize)]),
        _ => node?.DeepClone(),
    };
}
