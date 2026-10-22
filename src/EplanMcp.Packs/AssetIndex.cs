using System.Security;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs;

/// <summary>
/// The resource index of a pack version (M6 design 4.1.3, 4.5.1): generated on demand from its manifest and knowledge
/// documents, never stored, so it cannot be out of date. Reads no other file and hashes nothing; a knowledge document
/// that cannot be read is still an entry, with the reason (the Backstage way: load problems are status, not errors).
/// </summary>
public static class AssetIndex
{
    public static async Task<IReadOnlyList<AssetEntry>> ReadAsync(string folder, PackManifest manifest, CancellationToken cancellationToken)
    {
        string version = AssetNames.VersionOf(manifest);
        Dictionary<string, KnowledgeStanding> standings = new(PackPath.Comparer);
        foreach (PackSlot slot in manifest.Rules.Concat(manifest.Mappings).Concat(manifest.Docs))
        {
            standings[slot.Path] = slot.Standing;
        }

        List<AssetEntry> entries = [];
        foreach (PackResource resource in manifest.Resources)
        {
            (KnowledgeDocument? document, string? problem) = resource.Path.StartsWith("knowledge/", StringComparison.Ordinal) && resource.Storage == ResourceStorage.Bundled
                ? await DocumentAsync(Path.Combine(folder, resource.Path.Replace('/', Path.DirectorySeparatorChar)), cancellationToken).ConfigureAwait(false)
                : (null, null);
            entries.Add(new AssetEntry(manifest.Id, version, resource, document, problem, standings.TryGetValue(resource.Path, out KnowledgeStanding standing) ? standing : null));
        }

        return entries;
    }

    private static async Task<(KnowledgeDocument?, string?)> DocumentAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return (KnowledgeDocument.Read(await PackVerifier.ReadSmallAsync(path, Path.GetFileName(path), cancellationToken).ConfigureAwait(false)), null);
        }
        catch (PackFormatException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return (null, "the document cannot be read");
        }
    }
}
