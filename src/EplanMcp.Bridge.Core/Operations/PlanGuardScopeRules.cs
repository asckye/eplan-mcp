using System.Globalization;
using System.Text.RegularExpressions;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

internal static class PlanGuardScopeRules
{
    public static bool Page(PlanGuardScope scope, string page) =>
        (scope.Pages.Count + scope.Structures.Count + scope.Locators.Count > 0)
        && (scope.Pages.Count == 0 || scope.Pages.Contains(page, StringComparer.Ordinal))
        && (scope.Structures.Count == 0 || scope.Structures.Any(s => Prefix(page, s)))
        && (scope.Locators.Count == 0 || scope.Locators.Any(l => (l.Kind == "page" ? l.Name : l.Page) == page));

    public static bool Contains(PlanGuardScope scope, SnapshotObject obj) =>
        Page(scope, (obj.Kind == "page" ? obj.Name : obj.Page) ?? string.Empty)
        && (scope.Locators.Count == 0 || scope.Locators.Any(l => l.Kind == obj.Kind && l.Type == obj.Type && l.Oid == obj.Oid));

    private static bool Prefix(string page, string prefix)
    {
        string[] actual = Regex.Split(page.Split('/')[0], @"(?=[=+&#])");
        string[] wanted = Regex.Split(prefix, @"(?=[=+&#])");
        return wanted.Length <= actual.Length && actual.Take(wanted.Length).SequenceEqual(wanted, StringComparer.Ordinal);
    }

    // Match P06's System.Text.Json default escaping, including HTML-sensitive ASCII and uppercase Unicode escapes.
    public static string Members(string project, IEnumerable<SnapshotObject> objects)
    {
        string path = System.IO.Path.GetFullPath(project);
        string key = (path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - 4) : path).ToUpperInvariant();
        string quoted = JsonConvert.ToString(key, '"', StringEscapeHandling.EscapeNonAscii);
        quoted = Regex.Replace(quoted, @"\\u[0-9a-fA-F]{4}|[&'<>+`\u007f]", m => m.Length > 1
            ? "\\u" + m.Value.Substring(2).ToUpperInvariant() : "\\u" + ((int)m.Value[0]).ToString("X4", CultureInfo.InvariantCulture));
        string[] rows = [.. objects.Select(o => JsonConvert.SerializeObject(new[]
        {
            string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}", o.Kind, o.Type, o.Oid), o.Hash,
        })).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal)];
        return SnapshotCanonical.Sha256(string.Join("\n", new[] { SnapshotProfiles.Format + " " + SnapshotProfiles.StandardName, "members", quoted }.Concat(rows)));
    }

    public static JToken Canonical(object value) => Sort(BridgeJson.FromObject(value));
    private static JToken Sort(JToken value) => value is JObject obj
        ? new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Sort(p.Value))))
        : value is JArray array ? new JArray(array.Select(Sort)) : value.DeepClone();
}
