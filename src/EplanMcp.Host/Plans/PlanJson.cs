using System.Text.Json;
using System.Text.Json.Serialization;

namespace EplanMcp.Host.Plans;

/// <summary>Strict record reading, independent of the permissive Bridge/tool JSON settings.</summary>
internal static class PlanJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        AllowOutOfOrderMetadataProperties = true, // Canonical ordinal ordering can place the union tag last.
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static T Read<T>(string json) where T : class
    {
        // Parsing first also rejects duplicate members inside operation-specific JsonElement payloads.
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowDuplicateProperties = false });
        return document.Deserialize<T>(Options) ?? throw new JsonException("A plan record cannot be null.");
    }
}
