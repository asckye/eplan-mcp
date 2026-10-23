using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Plans;

/// <summary>Design 4.4 page/chunk formula; project-bound sets use unambiguous JSON rows (phase 2 design 4.3).</summary>
internal static class PlanSnapshotHashes
{
    public static string ProjectKey(string path)
    {
        string full = Path.GetFullPath(path);
        return (full.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? full[..^4] : full).ToUpperInvariant();
    }

    public static string Key(string kind, int type, long oid) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}/{type}/{oid}");

    public static string Page(IEnumerable<SnapshotObject> objects) => Content(objects.Select(o =>
        string.Create(CultureInfo.InvariantCulture, $"{o.Kind}\t{o.Type}\t{o.Oid}\t{o.Hash}")));

    public static string Compose(IEnumerable<SnapshotPage> pages) => Content(pages.Select(p =>
        string.Create(CultureInfo.InvariantCulture, $"{p.Type}\t{p.Oid}\t{p.Hash}")));

    public static string Members(string project, IEnumerable<KeyValuePair<string, string>> members) =>
        Set("members", project, members.Select(m => JsonSerializer.Serialize(new[] { m.Key, m.Value })));

    public static string Names(string project, IEnumerable<PlanOccupiedName> names) =>
        Set("names", project, names.Select(n => JsonSerializer.Serialize(new[] { n.Kind, n.Name })));

    private static string Set(string domain, string project, IEnumerable<string> rows) =>
        Hash([SnapshotProfiles.Format + " " + SnapshotProfiles.StandardName, domain, JsonSerializer.Serialize(ProjectKey(project)),
            .. rows.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);

    private static string Content(IEnumerable<string> rows) =>
        Hash([SnapshotProfiles.Format + " " + SnapshotProfiles.StandardName, .. rows.Order(StringComparer.Ordinal)]);

    private static string Hash(IEnumerable<string> rows) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
}
