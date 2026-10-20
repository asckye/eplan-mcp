using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The optional <c>pages</c> of eplan_check_run and eplan_report_update (tool specs, section 2): omitted for the whole
/// project, otherwise 1 to 50 full page names, each found as eplan_page_get finds a page: exactly, or ignoring case when
/// only one page matches.
/// </summary>
internal static class PageScope
{
    public const int MaxPages = 50;

    /// <summary>Adds every problem of <paramref name="pages"/> to <paramref name="violations"/>; null has none.</summary>
    public static void Check(List<string>? pages, List<string> violations)
    {
        if (pages is null)
        {
            return;
        }

        if (pages.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {MaxPages.ToString(CultureInfo.InvariantCulture)} full page names; omit it for the whole project.");
        }

        for (int i = 0; i < pages.Count && i < MaxPages; i++)
        {
            string at = "pages[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            int same = pages.FindIndex(0, i, p => string.Equals(p, pages[i], StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(pages[i]))
            {
                violations.Add($"{at} is required: a full page name as eplan_page_list returns it.");
            }
            else if (same >= 0)
            {
                violations.Add($"{at} repeats pages[{same.ToString(CultureInfo.InvariantCulture)}] (letter case does not count).");
            }
        }
    }

    /// <summary>
    /// The pages' full names as EPLAN has them, in the order given; null for the whole project. PAGE_NOT_FOUND lists
    /// every name not found, AMBIGUOUS_NAME the candidates of one that matches several pages ignoring case.
    /// </summary>
    public static IReadOnlyList<string>? Resolve(List<string>? pages, OpenProject project, IPageReader reader)
    {
        if (pages is null)
        {
            return null;
        }

        IReadOnlyList<string> names = reader.PageNames(project.Path);
        string?[] found = [.. pages.Select(page => PageUpdateRequest.Find(names, page))];
        string[] missing = [.. pages.Where((_, i) => found[i] is null)];
        return missing.Length > 0
            ? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has these names, so nothing was changed. Use eplan_page_list with name_prefix to find the exact page names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["missing"] = string.Join("\n", missing) })
            : [.. found.OfType<string>()];
    }
}
