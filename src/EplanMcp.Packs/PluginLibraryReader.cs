using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>Strict, bounded parsing of untrusted library text without file access.</summary>
public static class PluginLibraryReader
{
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxEntries = 2000;
    public const string FileName = "library.json";

    public static PluginLibraryIndex Read(byte[] utf8)
    {
        Require(utf8.Length <= MaxFileBytes, "index exceeds 1 MiB");
        using JsonDocument document = PackJson.Parse(utf8.AsMemory(utf8.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0), FileName);
        PluginLibraryIndex index;
        try
        {
            index = document.Deserialize<PluginLibraryIndex>(PackJson.Options)
                ?? throw new PackFormatException("library.json must be an object.");
        }
        catch (JsonException ex)
        {
            throw new PackFormatException($"library.json is invalid: {ex.Message}", ex);
        }

        Require(Id(index.Name), "name must be a pack-style identifier");
        Require(index.Description is null || Text(index.Description, 500), "description must be 1 to 500 characters without controls");
        Require(index.Plugins.Count <= MaxEntries, "plugins exceeds 2000 entries");
        var identities = new HashSet<(string Id, string Version)>();
        var files = new HashSet<string>(PackPath.Comparer);
        foreach (PluginLibraryEntry entry in index.Plugins)
        {
            Require(entry is not null, "plugins must not contain null");
            Require(Id(entry!.Id), "plugin id must be a pack-style identifier");
            Require(Text(entry.Version, 128) && SemanticVersion.TryParse(entry.Version, out _), "plugin version must be SemVer");
            Require(Enum.IsDefined(entry.Kind), "plugin kind must be supported");
            Require(entry.Digest.Length == 64 && PackManifestValidator.IsSha256(entry.Digest), "plugin digest must be lowercase SHA-256");
            Require(PackPath.Problem(entry.File) is null && !entry.File.Contains('/', StringComparison.Ordinal), "plugin file must be one safe relative filename");
            Require(Text(entry.Title, 500), "plugin title must be 1 to 500 characters without controls");
            Require(entry.Standards.Count <= 64 && entry.Standards.All(s => Text(s, 128))
                && entry.Standards.Distinct(StringComparer.Ordinal).Count() == entry.Standards.Count, "standards must contain at most 64 unique labels of 1 to 128 characters");
            Require(entry.Kind == PackKind.Standard || entry.Standards.Count == 0, "only standard plugins may declare standards");
            Require(identities.Add((entry.Id, entry.Version)), "duplicate plugin identity");
            Require(files.Add(entry.File), "duplicate plugin filename");
        }

        return index;
    }

    private static bool Id(string value) => !value.Any(char.IsControl) && PackManifestValidator.IsId(value);

    private static bool Text(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);

    private static void Require(bool condition, string problem)
    {
        if (!condition)
        {
            throw new PackFormatException($"library.json: {problem}.");
        }
    }
}
