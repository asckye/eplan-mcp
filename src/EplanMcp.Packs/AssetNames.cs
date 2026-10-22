using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Packs.Scanning;

namespace EplanMcp.Packs;

/// <summary>
/// How agents name a pack's resources (M6 design 4.1.3): the asset id <c>&lt;pack&gt;@&lt;version&gt;:&lt;kind&gt;/&lt;name&gt;</c>,
/// the version <c>dev</c> for a development pack, the kind in snake_case. A knowledge document at
/// <c>knowledge/&lt;kind&gt;/&lt;name&gt;.json</c> is named by its kind and name; another resource by its kind and its
/// path below <c>resources/&lt;kind folder&gt;/</c> (the whole path when it lies elsewhere). Inside a pack, documents name
/// its own assets by the local part <c>&lt;kind&gt;/&lt;name&gt;</c>, so building a version changes no file.
/// </summary>
public static partial class AssetNames
{
    public const string Dev = "dev";

    /// <summary>The local part of <paramref name="resource"/>'s asset id.</summary>
    public static string Local(PackResource resource)
    {
        if (KnowledgePath().Match(resource.Path) is { Success: true } knowledge)
        {
            return knowledge.Groups[1].Value + "/" + knowledge.Groups[2].Value;
        }

        string prefix = $"resources/{ResourceClassifier.Folder(resource.Kind)}/";
        string name = resource.Path.StartsWith(prefix, StringComparison.Ordinal) ? resource.Path[prefix.Length..] : resource.Path;
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(resource.Kind.ToString()) + "/" + name;
    }

    /// <summary>The kind part of a local name, such as <c>macro</c> or <c>candidate</c>.</summary>
    public static string KindOf(string local) => local[..local.IndexOf('/', StringComparison.Ordinal)];

    public static string Id(string pack, string version, string local) => $"{pack}@{version}:{local}";

    /// <summary>The version an id shows for <paramref name="manifest"/>: <c>dev</c> for a development pack.</summary>
    public static string VersionOf(PackManifest manifest) => manifest.Version == DevPackStore.ManifestVersion ? Dev : manifest.Version;

    /// <summary>Splits an asset id; false when it does not have the form. Whether the pack and version exist is not checked.</summary>
    public static bool TryParse(string? id, [NotNullWhen(true)] out string? pack, [NotNullWhen(true)] out string? version, [NotNullWhen(true)] out string? local)
    {
        Match match = AssetId().Match(id ?? string.Empty);
        (pack, version, local) = match.Success ? (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value) : (null, null, null);
        return match.Success;
    }

    [GeneratedRegex(@"^knowledge/(candidate|module|material_mapping|gap)/([^/]+)\.json$", RegexOptions.ECMAScript)]
    private static partial Regex KnowledgePath();

    [GeneratedRegex(@"^([a-z0-9]+(?:[.-][a-z0-9]+)*)@([^:@/]+):([a-z_]+/.+)$", RegexOptions.ECMAScript)]
    private static partial Regex AssetId();
}
