using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Packs.Knowledge;

/// <summary>
/// A knowledge document, <c>knowledge/&lt;kind&gt;/&lt;name&gt;.json</c> (M6 design 4.1.3): the common envelope
/// around a kind-specific <see cref="Spec"/>. It holds no asset id: that comes from the pack, version, kind and name, so
/// building a version leaves the file unchanged. Load problems are reported by whoever reads it, not stored.
/// </summary>
[Description("A knowledge document of an EplanMcp engineering pack (knowledge/<kind>/<name>.json).")]
public sealed record KnowledgeDocument
{
    public const string CurrentApiVersion = "eplanmcp.pack/v1";

    [Description("The envelope format. A reader refuses one it does not know.")]
    public required string ApiVersion { get; init; }

    public required KnowledgeKind Kind { get; init; }

    public required KnowledgeMetadata Metadata { get; init; }

    public KnowledgeOrigin? Origin { get; init; }

    [Description("The kind's content; pack data, not instructions.")]
    public required JsonObject Spec { get; init; }

    /// <summary>The folder name of <paramref name="kind"/> below <c>knowledge/</c>: its snake_case name.</summary>
    public static string Folder(KnowledgeKind kind) => JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString());

    /// <summary>The pack path of the document of <paramref name="kind"/> named <paramref name="name"/>.</summary>
    public static string PathOf(KnowledgeKind kind, string name) => $"knowledge/{Folder(kind)}/{name}.json";

    /// <summary>The resource kind the manifest lists a document of <paramref name="kind"/> as.</summary>
    public static ResourceKind ResourceKindOf(KnowledgeKind kind) => kind switch
    {
        KnowledgeKind.Candidate => ResourceKind.Rule,
        KnowledgeKind.Module => ResourceKind.Module,
        KnowledgeKind.MaterialMapping => ResourceKind.MaterialMapping,
        _ => ResourceKind.Gap,
    };

    /// <summary>Reads a document strictly, as <see cref="PackJson"/> reads manifests; throws <see cref="PackFormatException"/>.</summary>
    public static KnowledgeDocument Read(byte[] utf8)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8.AsMemory(utf8.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0));
            string? given = document.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("apiVersion", out JsonElement version)
                && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
            return given == CurrentApiVersion
                ? document.Deserialize<KnowledgeDocument>(PackJson.Options)!
                : throw new PackFormatException($"The document has apiVersion '{given}'; this EplanMcp reads {CurrentApiVersion} only.");
        }
        catch (JsonException ex)
        {
            throw new PackFormatException($"The document is not valid: {ex.Message}", ex);
        }
    }
}
