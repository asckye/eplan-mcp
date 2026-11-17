using System.Globalization;
using System.Text;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>PlanSnapshotHashes.Names' existing encoding, without loading System.Text.Json into EPLAN (ADR-0003).</summary>
public static class PlanNamesDigest
{
    public static string Names(string project, IEnumerable<PlanNameEntry> names)
    {
        string path = Path.GetFullPath(project);
        string key = (path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - 4) : path).ToUpperInvariant();
        IEnumerable<string> rows = names.Select(n => "[" + Quote(n.Kind) + "," + Quote(n.Name) + "]")
            .Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal);
        return SnapshotCanonical.Sha256(string.Join("\n", new[]
        {
            SnapshotProfiles.Format + " " + SnapshotProfiles.StandardName, "names", Quote(key),
        }.Concat(rows)));
    }

    // System.Text.Json's default encoder escapes non-ASCII UTF-16 units and HTML-sensitive ASCII.
    private static string Quote(string value)
    {
        var text = new StringBuilder("\"");
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            string? escaped = c switch
            {
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => null,
            };
            bool unpaired = char.IsSurrogate(c) && !(char.IsHighSurrogate(c)
                ? i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) : i > 0 && char.IsHighSurrogate(value[i - 1]));
            text.Append(escaped ?? (c is >= ' ' and <= '~' and not ('"' or '\'' or '&' or '+' or '<' or '>' or '`')
                ? c.ToString() : "\\u" + ((int)(unpaired ? '\uFFFD' : c)).ToString("X4", CultureInfo.InvariantCulture)));
        }

        return text.Append('"').ToString();
    }
}
