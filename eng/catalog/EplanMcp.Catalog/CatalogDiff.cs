using System.Globalization;
using System.Text;
using System.Text.Json;

namespace EplanMcp.Catalog;

/// <summary>One documentation comment ID found in only one of two catalogs, and whether the Bridge uses it.</summary>
internal sealed record CatalogDifference(string Side, string Id, bool UsedByBridge);

/// <summary>
/// <c>diff --base &lt;catalog&gt; --other &lt;catalog&gt; --out &lt;tsv&gt; [--usage &lt;bridge-usage.json&gt;]</c>: the IDs that
/// exist in only one of two catalog folders written by <c>extract</c>, for example two EPLAN versions. Every ID is the
/// XML documentation comment ID of the member, so the folded forms of the catalog are unfolded first: enum values kept
/// in their type's line become <c>F:&lt;enum type&gt;.&lt;value&gt;</c>, and data dictionary lines become
/// <c>F:Eplan.EplApi.DataModel.Properties.&lt;object&gt;.&lt;name&gt;</c>, the IDs that
/// catalog/mapping/bridge-usage.json uses. A changed signature shows as one ID on each side. The output is sorted by side
/// and ID with ordinal comparison, so the same input always gives the same file.
/// </summary>
internal static class CatalogDiff
{
    internal const string BaseOnly = "base_only";
    internal const string OtherOnly = "other_only";
    private const string PropertiesPrefix = "F:Eplan.EplApi.DataModel.Properties.";

    public static IReadOnlyList<CatalogDifference> Compare(string baseFolder, string otherFolder, IReadOnlySet<string> used)
    {
        HashSet<string> baseIds = Ids(baseFolder);
        HashSet<string> otherIds = Ids(otherFolder);
        return
        [
            .. baseIds.Where(id => !otherIds.Contains(id)).Order(StringComparer.Ordinal).Select(id => new CatalogDifference(BaseOnly, id, used.Contains(id))),
            .. otherIds.Where(id => !baseIds.Contains(id)).Order(StringComparer.Ordinal).Select(id => new CatalogDifference(OtherOnly, id, used.Contains(id))),
        ];
    }

    /// <summary>Every ID of a catalog folder, enum values and data dictionary properties unfolded.</summary>
    public static HashSet<string> Ids(string folder)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Path.Combine(folder, "types.jsonl")))
        {
            using JsonDocument type = JsonDocument.Parse(line);
            string id = type.RootElement.GetProperty("id").GetString()!;
            ids.Add(id);
            if (type.RootElement.TryGetProperty("values", out JsonElement values))
            {
                foreach (JsonProperty value in values.EnumerateObject())
                {
                    ids.Add("F:" + id[2..] + "." + value.Name);
                }
            }
        }

        foreach (string file in Directory.EnumerateFiles(Path.Combine(folder, "members"), "*.jsonl"))
        {
            foreach (string line in File.ReadLines(file))
            {
                using JsonDocument member = JsonDocument.Parse(line);
                ids.Add(member.RootElement.GetProperty("id").GetString()!);
            }
        }

        foreach (string line in File.ReadLines(Path.Combine(folder, "data-dictionary.jsonl")))
        {
            using JsonDocument property = JsonDocument.Parse(line);
            ids.Add(PropertiesPrefix + property.RootElement.GetProperty("object").GetString() + "." + property.RootElement.GetProperty("name").GetString());
        }

        return ids;
    }

    /// <summary>The IDs in the <c>usages</c> object of catalog/mapping/bridge-usage.json.</summary>
    public static HashSet<string> Usages(string bridgeUsage)
    {
        using JsonDocument usage = JsonDocument.Parse(File.ReadAllText(bridgeUsage));
        return usage.RootElement.TryGetProperty("usages", out JsonElement usages)
            ? usages.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Tab-separated, with a header line and LF line ends.</summary>
    public static void Write(IEnumerable<CatalogDifference> differences, string path)
    {
        var text = new StringBuilder("side\tid\tused_by_bridge\n");
        foreach (CatalogDifference d in differences)
        {
            text.Append(CultureInfo.InvariantCulture, $"{d.Side}\t{d.Id}\t{(d.UsedByBridge ? "yes" : "no")}\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>One line per side: all IDs, then those the Bridge uses.</summary>
    public static string Summary(IReadOnlyList<CatalogDifference> differences) => string.Join(Environment.NewLine,
        new[] { BaseOnly, OtherOnly }.Select(side => string.Create(CultureInfo.InvariantCulture,
            $"{side}: {differences.Count(d => d.Side == side)} IDs, {differences.Count(d => d.Side == side && d.UsedByBridge)} used by the Bridge")));
}
