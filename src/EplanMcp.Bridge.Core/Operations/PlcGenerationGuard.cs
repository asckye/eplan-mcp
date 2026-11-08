using System.Globalization;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Checks that PLC generation preserves placed function identities and only creates requested pages.</summary>
internal static class PlcGenerationGuard
{
    public static IReadOnlyList<string> Problems(
        IReadOnlyDictionary<long, string?> beforeFunctions,
        IReadOnlyDictionary<long, string?> afterFunctions,
        IEnumerable<string> beforePages,
        IEnumerable<string> afterPages,
        IEnumerable<string> requestedPages)
    {
        var problems = new List<string>();
        foreach (KeyValuePair<long, string?> function in beforeFunctions)
        {
            if (function.Value is null)
            {
                continue;
            }

            afterFunctions.TryGetValue(function.Key, out string? page);
            if (!string.Equals(function.Value, page, StringComparison.Ordinal))
            {
                problems.Add("Function " + function.Key.ToString(CultureInfo.InvariantCulture) + " on '" + function.Value
                    + "' is " + (page is null ? "no longer placed." : "now on '" + page + "'."));
            }
        }

        var allowedPages = new HashSet<string>(beforePages, StringComparer.Ordinal);
        allowedPages.UnionWith(requestedPages);
        problems.AddRange(afterPages.Where(page => !allowedPages.Contains(page))
            .Select(page => "Unrequested page '" + page + "' was created."));
        return problems;
    }
}
