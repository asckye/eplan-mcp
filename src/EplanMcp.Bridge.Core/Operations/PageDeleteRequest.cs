using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of page.delete.plan and page.delete.execute, and what the deletion touches; both operations
/// build them the same way, so they agree on the pages, the devices left without a main function and the fingerprint
/// (docs/tools/eplan_page_delete.md, section 5).
/// </summary>
internal sealed class PageDeleteRequest
{
    public const int MaxPages = 50;
    public const int MaxOrphaned = 50;
    public const int MaxOrphanPages = 10;

    private PageDeleteRequest(OpenProject project, List<string> names)
    {
        Project = project;
        Names = names;
    }

    public OpenProject Project { get; }

    public IReadOnlyList<string> Names { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT (mcp-tool-design 6.2), then the project.</summary>
    public static PageDeleteRequest Check(PageDeleteArgs args, IProjectCatalog catalog)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<string> names = args.Pages ?? [];
        if (names.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {Number(MaxPages)} full page names.");
        }

        for (int i = 0; i < names.Count && i < MaxPages; i++)
        {
            int same = names.FindIndex(0, i, n => string.Equals(n, names[i], StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(names[i]))
            {
                violations.Add($"pages[{Number(i)}] is empty; give a full page name such as =GAA+A1&EFS1/60 (eplan_page_list).");
            }
            else if (same >= 0)
            {
                violations.Add($"pages[{Number(i)}] repeats pages[{Number(same)}] (letter case does not count).");
            }
        }

        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : new PageDeleteRequest(WriteArguments.Writable(catalog, args.Project), names);
    }

    /// <summary>
    /// The pages by their exact names (no fallback ignoring case: only pages the model named are deleted) and what their
    /// deletion touches; PAGE_NOT_FOUND names every page that is missing.
    /// </summary>
    public PageDeletion Read(IPageRemover remover)
    {
        var pages = new List<RemovablePage>();
        var missing = new List<string>();
        foreach (string name in Names)
        {
            if (remover.ReadPage(Project.Path, name) is { } page)
            {
                pages.Add(page);
            }
            else
            {
                missing.Add(name);
            }
        }

        return missing.Count == 0
            ? new PageDeletion(pages, remover.Functions(Project.Path))
            : throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                $"Project '{Project.Name}' has no page with exactly these names (letter case counts): {string.Join(", ", missing)}. Nothing was deleted. Use eplan_page_list to find the exact names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["missing"] = string.Join("\n", missing) });
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// What deleting <see cref="Pages"/> touches: the functions on them, and the devices whose main function is on one of
/// them while functions with the same full tag stay on other pages, which have no main function of that tag (spike S12,
/// api-notes A18: a cable's cores).
/// </summary>
internal sealed class PageDeletion
{
    public PageDeletion(IReadOnlyList<RemovablePage> pages, IReadOnlyList<PlacedFunction> functions)
    {
        var deleted = new HashSet<string>(pages.Select(p => p.Name), StringComparer.Ordinal);
        Pages = pages;
        FunctionCounts = [.. pages.Select(p => functions.Count(f => string.Equals(f.Page, p.Name, StringComparison.Ordinal)))];
        MainTags = [.. functions.Where(f => f.IsMain && f.Name.Length > 0 && f.Page is not null && deleted.Contains(f.Page)).Select(f => f.Name).Distinct(StringComparer.Ordinal)];
        List<PlacedFunction> staying = [.. functions.Where(f => f.Page is not null && !deleted.Contains(f.Page))];
        Orphaned = Orphans(MainTags, staying);
        var orphaned = new HashSet<string>(Orphaned.Select(o => o.Tag), StringComparer.Ordinal);
        Fingerprint = Operations.Fingerprint.Of(
            pages.SelectMany(p => p.ObjectIds.Select(id => "page\t" + p.Name + "\t" + id))
                .Concat(staying.Where(f => orphaned.Contains(f.Name)).Select(f => "left\t" + f.Name + "\t" + f.Id)));
    }

    public IReadOnlyList<RemovablePage> Pages { get; }

    /// <summary>How many functions are placed on each page, in the order of <see cref="Pages"/>.</summary>
    public IReadOnlyList<int> FunctionCounts { get; }

    /// <summary>The full tags of the main functions on the pages.</summary>
    public IReadOnlyList<string> MainTags { get; }

    /// <summary>Every device the deletion leaves without a main function, in EPLAN's order.</summary>
    public IReadOnlyList<OrphanedDevice> Orphaned { get; }

    /// <summary>
    /// The plan's state: the session identifiers of the pages and of everything on them, and of the functions the
    /// orphaned devices keep on other pages.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// The devices among <paramref name="tags"/> that have functions in <paramref name="placed"/> but no main function
    /// there, with how many functions and on which pages (at most <see cref="PageDeleteRequest.MaxOrphanPages"/>).
    /// </summary>
    public static List<OrphanedDevice> Orphans(IEnumerable<string> tags, IEnumerable<PlacedFunction> placed)
    {
        ILookup<string, PlacedFunction> byTag = placed.Where(f => f.Page is not null).ToLookup(f => f.Name, StringComparer.Ordinal);
        return
        [
            .. tags.Where(tag => byTag[tag].Any() && !byTag[tag].Any(f => f.IsMain)).Select(tag => new OrphanedDevice
            {
                Tag = tag,
                FunctionsLeft = byTag[tag].Count(),
                Pages = [.. byTag[tag].Select(f => f.Page!).Distinct(StringComparer.Ordinal).Take(PageDeleteRequest.MaxOrphanPages)],
            }),
        ];
    }
}
