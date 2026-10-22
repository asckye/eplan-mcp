using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Xml;

namespace EplanMcp.Catalog;

/// <summary>One file looked at in the SDK folder; <see cref="Skipped"/> says why it is not in the catalog.</summary>
internal sealed record AssemblyInfo(
    string File, string? Name, string? Version, string? FileVersion, string? Xml, int Entries, string? Skipped);

/// <summary>How the IntelliSense IDs relate to the catalog: public API, non-public, outside the prefix, or unexplained.</summary>
internal sealed record XmlStats(int Members, int Public, int NonPublic, int OutsideNamespace, IReadOnlyList<string> Unmatched);

internal sealed record CatalogData(
    string NamespacePrefix, List<AssemblyInfo> Assemblies, List<CatalogEntry> Entries, XmlStats Xml, int OutsideTypes, int Duplicates);

/// <summary>Builds the catalog from the API assemblies that have an IntelliSense file next to them.</summary>
internal static class Extractor
{
    public static CatalogData Extract(string sdk, string namespacePrefix)
    {
        var assemblies = new List<AssemblyInfo>();
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
        var nonPublic = new HashSet<string>(StringComparer.Ordinal);
        var xmlIds = new HashSet<string>(StringComparer.Ordinal);
        int outsideTypes = 0, duplicates = 0;
        foreach (string path in Directory.EnumerateFiles(sdk, "Eplan.EplApi.*")
            .Where(f => Path.GetExtension(f) is ".dll" or ".erx").Order(StringComparer.Ordinal))
        {
            string file = Path.GetFileName(path);
            string xml = Path.ChangeExtension(path, ".xml");
            if (!File.Exists(xml) || !HasMetadata(path))
            {
                assemblies.Add(new(file, null, null, null, null, 0, File.Exists(xml) ? "no_metadata" : "no_intellisense_file"));
                continue;
            }

            ScannedAssembly scanned = AssemblyScanner.Scan(path, namespacePrefix);
            foreach (CatalogEntry entry in scanned.Entries)
            {
                duplicates += entries.TryAdd(entry.Id, entry) ? 0 : 1;
            }

            nonPublic.UnionWith(scanned.NonPublicIds);
            xmlIds.UnionWith(ReadXmlIds(xml));
            outsideTypes += scanned.OutsideTypes;
            assemblies.Add(new(file, scanned.Name, scanned.Version, FileVersionInfo.GetVersionInfo(path).FileVersion,
                Path.GetFileName(xml), scanned.Entries.Count, null));
        }

        foreach (CatalogEntry entry in entries.Values)
        {
            entry.Documented = xmlIds.Contains(entry.Id);
            Classifier.Classify(entry);
        }

        var unmatched = new List<string>();
        int isPublic = 0, isNonPublic = 0, outside = 0;
        foreach (string id in xmlIds.Order(StringComparer.Ordinal))
        {
            if (entries.ContainsKey(id))
            {
                isPublic++;
            }
            else if (nonPublic.Contains(id) || nonPublic.Contains(DeclaringTypeId(id)))
            {
                // A non-public member, or a member of a non-public type: native C++ members have no metadata row.
                isNonPublic++;
            }
            else if (!id[2..].StartsWith(namespacePrefix + ".", StringComparison.Ordinal))
            {
                outside++;
            }
            else
            {
                unmatched.Add(id);
            }
        }

        return new(namespacePrefix, assemblies, [.. entries.Values.OrderBy(e => e.Id, StringComparer.Ordinal)],
            new XmlStats(xmlIds.Count, isPublic, isNonPublic, outside, unmatched), outsideTypes, duplicates);
    }

    /// <summary>T:A.B for M:A.B.Name(...), P:A.B.Name or F:A.B.Name; empty when the name has no dot.</summary>
    internal static string DeclaringTypeId(string id)
    {
        string name = id[2..].Split('(', '~')[0];
        int dot = name.LastIndexOf('.');
        return dot < 0 ? "" : "T:" + name[..dot];
    }

    private static bool HasMetadata(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return pe.HasMetadata;
    }

    private static List<string> ReadXmlIds(string path)
    {
        var ids = new List<string>();
        using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.ReadToFollowing("member"))
        {
            if (reader.GetAttribute("name") is { } name)
            {
                ids.Add(name);
            }
        }

        return ids;
    }
}
