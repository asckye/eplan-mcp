using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace EplanMcp.Packs;

/// <summary>
/// How pack files are written and read. Writing gives the same bytes for the same value: UTF-8 without BOM, two spaces,
/// LF, a final newline, properties in declaration order, no nulls. Reading is strict: unknown or duplicate properties,
/// missing required ones and nulls where none is allowed are errors. The format version is checked first, so a newer
/// file is refused for its version, not for its first unknown property.
/// </summary>
public static class PackJson
{
    /// <summary>The longest pack file (manifest, lock, bindings) EplanMcp reads.</summary>
    public const int MaxFileBytes = 16 * 1024 * 1024;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] Serialize<T>(T value) => [.. JsonSerializer.SerializeToUtf8Bytes(value, Options), (byte)'\n'];

    public static PackManifest ReadManifest(byte[] utf8) =>
        Read<PackManifest>(utf8, PackManifest.FileName, "schemaVersion", 1, 2, ValidateManifest, 3);

    private static void ValidateManifest(JsonElement root)
    {
        if (root.GetProperty("schemaVersion").GetInt32() == 3 && !root.TryGetProperty("kind", out _))
        {
            throw new JsonException("A schemaVersion 3 manifest requires kind.");
        }
        if (root.TryGetProperty("standards", out _) && (!root.TryGetProperty("kind", out JsonElement kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "standard"))
        {
            throw new JsonException("standards is only allowed for standard plugins.");
        }
        foreach (string name in new[] { "provides", "standards" })
        {
            if (root.TryGetProperty(name, out JsonElement labels) && labels.ValueKind == JsonValueKind.Array && labels.EnumerateArray().Any(label => label.ValueKind != JsonValueKind.String))
            {
                throw new JsonException($"{name} must contain strings only.");
            }
        }
    }

    public static ProjectBinding ReadBinding(byte[] utf8) =>
        Read<ProjectBinding>(utf8, ProjectBinding.FileName, "bindingVersion", ProjectBinding.CurrentBindingVersion, 2, BindingReadModel.Validate);

    /// <summary>
    /// <paramref name="utf8"/>, the content of <paramref name="file"/>, as <typeparamref name="T"/> once its
    /// <paramref name="versionProperty"/> is <paramref name="version"/>. Throws <see cref="PackFormatException"/>.
    /// </summary>
    internal static T Read<T>(byte[] utf8, string file, string versionProperty, int version, int? nextVersion = null, Action<JsonElement>? validate = null, int? thirdVersion = null)
    {
        using JsonDocument document = Parse(utf8.AsMemory(utf8.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0), file);
        int? given = document.RootElement is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty(versionProperty, out JsonElement found)
            && found.ValueKind == JsonValueKind.Number && found.TryGetInt32(out int number) ? number : null;
        if (given != version && (nextVersion is null || given != nextVersion) && (thirdVersion is null || given != thirdVersion))
        {
            throw new PackFormatException(given is null
                ? $"{file} has no {versionProperty}; it is not a file of an EplanMcp pack."
                : $"{file} has {versionProperty} {given}; this EplanMcp reads {versionProperty} {version}{(nextVersion is null ? string.Empty : $" or {nextVersion}")}{(thirdVersion is null ? string.Empty : $" or {thirdVersion}")} only. A newer EplanMcp may read it. (/{versionProperty})");
        }

        try
        {
            validate?.Invoke(document.RootElement);
            return document.Deserialize<T>(Options)!;
        }
        catch (JsonException ex)
        {
            throw new PackFormatException($"{file} is not valid for {versionProperty} {given}: {ex.Message}", ex);
        }
    }

    internal static JsonDocument Parse(ReadOnlyMemory<byte> json, string file)
    {
        if (json.Length > MaxFileBytes)
        {
            throw new PackFormatException($"{file} is longer than 16 MB.");
        }

        try
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = Options.MaxDepth, AllowDuplicateProperties = false });
        }
        catch (JsonException ex)
        {
            throw new PackFormatException($"{file} is not valid JSON: {ex.Message}", ex);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            IndentSize = 2,
            NewLine = "\n",
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            MaxDepth = 32,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { type =>
                {
                    if (type.Type == typeof(PackManifest))
                    {
                        foreach (JsonPropertyInfo property in type.Properties.Where(p => p.Name is "kind" or "provides" or "standards"))
                        {
                            property.ShouldSerialize = (value, field) => ((PackManifest)value).SchemaVersion >= 3 && field is not null;
                        }
                    }
                } },
            },
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        options.MakeReadOnly();
        return options;
    }
}
