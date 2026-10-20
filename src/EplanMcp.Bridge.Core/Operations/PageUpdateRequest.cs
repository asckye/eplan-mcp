using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of page.update.plan and page.update.execute, with each page found by its name the way
/// eplan_page_get finds it: exactly, or ignoring case when only one page matches (docs/tools/eplan_page_update.md).
/// </summary>
internal sealed class PageUpdateRequest
{
    public const int MaxPages = 50;
    public const int MaxDescription = 500;

    private PageUpdateRequest(OpenProject project, string language, List<Page> pages)
    {
        Project = project;
        Language = language;
        Pages = pages;
    }

    public OpenProject Project { get; }

    /// <summary>The code of the language that changes, also when the tool's language was omitted.</summary>
    public string Language { get; }

    public IReadOnlyList<Page> Pages { get; }

    public static PageUpdateRequest Check(PageUpdateArgs args, IProjectCatalog catalog, IPageReader reader, IPageDescriptions descriptions)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<PageUpdateItem> items = args.Pages ?? [];
        if (items.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {MaxPages.ToString(CultureInfo.InvariantCulture)} pages.");
        }

        for (int i = 0; i < items.Count && i < MaxPages; i++)
        {
            string at = "pages[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            int same = items.FindIndex(0, i, p => string.Equals(p.Name, items[i].Name, StringComparison.OrdinalIgnoreCase));
            AddIf(violations, string.IsNullOrEmpty(items[i].Name), $"{at}.name is required: a full page name as eplan_page_list returns it.");
            AddIf(violations, same >= 0 && !string.IsNullOrEmpty(items[i].Name), $"{at}.name repeats pages[{same.ToString(CultureInfo.InvariantCulture)}].name (letter case does not count).");
            AddIf(violations, items[i].Description is null, $"{at}.description is required; an empty string clears it.");
            AddIf(violations, items[i].Description?.Length > MaxDescription, $"{at}.description must have at most {MaxDescription.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        string? language = descriptions.LanguageCode(string.IsNullOrWhiteSpace(args.Language) ? null : args.Language!.Trim());
        AddIf(violations, language is null, "'language' is not a language EPLAN knows; use a code like zh_CN or en_US, or omit it for EPLAN's user interface language.");
        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<string> names = reader.PageNames(project.Path);
        var missing = new List<string>();
        var pages = new List<Page>();
        foreach (PageUpdateItem item in items)
        {
            string? name = Find(names, item.Name);
            if (name is null)
            {
                missing.Add(item.Name);
            }
            else
            {
                pages.Add(new Page(name, item.Description.Length == 0 ? null : item.Description));
            }
        }

        return missing.Count > 0
            ? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has these names, so nothing was changed. Use eplan_page_list with name_prefix to find the exact page names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["missing"] = string.Join("\n", missing) })
            : new PageUpdateRequest(project, language!, pages);
    }

    /// <summary>The plan's state: each page's name and raw description, so any change to them is PLAN_STALE.</summary>
    public static string FingerprintOf(IEnumerable<(Page Page, PageDescription Before)> pages) =>
        Fingerprint.Of(pages.Select(p => p.Page.Name + "\t" + (p.Before.Raw ?? string.Empty)));

    /// <summary>The exact name, or the one name equal ignoring case; AMBIGUOUS_NAME when several are.</summary>
    private static string? Find(IReadOnlyList<string> names, string wanted)
    {
        if (names.Contains(wanted, StringComparer.Ordinal))
        {
            return wanted;
        }

        string[] matches = [.. names.Where(n => string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal)];
        return matches.Length > 1
            ? throw ProjectResolver.Ambiguous("page", wanted, matches, "Several pages have this name in different letter case; pass one of the candidates exactly.")
            : matches.FirstOrDefault();
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    /// <summary>One page to change: its name as EPLAN has it and the new text (null clears it).</summary>
    internal sealed class Page(string name, string? text)
    {
        public string Name { get; } = name;

        public string? Text { get; } = text;
    }
}
