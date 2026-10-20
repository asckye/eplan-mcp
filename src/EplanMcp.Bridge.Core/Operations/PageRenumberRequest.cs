using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of page.renumber.plan and page.renumber.execute and the plan's state; both build them the same
/// way, so they agree on the pages and the fingerprint (docs/tools/eplan_page_renumber.md, section 5).
/// </summary>
internal sealed class PageRenumberRequest
{
    public const int MaxPages = 50;

    private PageRenumberRequest(OpenProject project, PageRenumberArgs args, string subpages)
    {
        Project = project;
        Pages = args.Pages;
        Start = args.Start;
        Step = args.Step;
        KeepText = args.KeepText;
        Subpages = subpages;
    }

    public OpenProject Project { get; }

    public IReadOnlyList<string> Pages { get; }

    public int Start { get; }

    public int Step { get; }

    public bool KeepText { get; }

    /// <summary>One of <see cref="PageRenumberArgs.SubpageModes"/>.</summary>
    public string Subpages { get; }

    /// <summary>The numbering as the form shows it after the change list, e.g. <c>from 200, step 10, per structure, subpages: retain, keep text: no</c>.</summary>
    public string Numbering =>
        $"from {Number(Start)}, step {Number(Step)}, per structure, subpages: {Subpages}, keep text: {(KeepText ? "yes" : "no")}";

    /// <summary>Every argument problem at once as INVALID_ARGUMENT (mcp-tool-design 6.2), then the project.</summary>
    public static PageRenumberRequest Check(PageRenumberArgs args, IProjectCatalog catalog)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<string> pages = args.Pages ?? [];
        if (pages.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {Number(MaxPages)} full page names.");
        }

        for (int i = 0; i < pages.Count && i < MaxPages; i++)
        {
            int same = pages.FindIndex(0, i, p => string.Equals(p, pages[i], StringComparison.OrdinalIgnoreCase));
            AddIf(violations, string.IsNullOrWhiteSpace(pages[i]), $"pages[{Number(i)}] is empty; give a full page name such as =GAA+A1&EFS1/90 (eplan_page_list).");
            AddIf(violations, same >= 0 && !string.IsNullOrWhiteSpace(pages[i]), $"pages[{Number(i)}] repeats pages[{Number(same)}] (letter case does not count).");
        }

        AddIf(violations, args.Start < 1, "'start' must be at least 1.");
        AddIf(violations, args.Step < 1, "'step' must be at least 1.");
        string subpages = args.Subpages ?? PageRenumberArgs.SubpageModes[0];
        AddIf(violations, !PageRenumberArgs.SubpageModes.Contains(subpages), $"'subpages' must be one of {string.Join(", ", PageRenumberArgs.SubpageModes)}.");
        return violations.Count > 0 ? throw WriteArguments.Invalid(violations) : new PageRenumberRequest(WriteArguments.Writable(catalog, args.Project), args, subpages);
    }

    /// <summary>
    /// The plan's state: each page's session identifier and name, found by its exact name (PAGE_NOT_FOUND names every
    /// missing one), and the names of all pages in their structures, since whether a new number is free depends on them.
    /// </summary>
    public string FingerprintOf(IPageRenumberer renumberer, IPageReader pages)
    {
        var lines = new List<string>();
        var missing = new List<string>();
        foreach (string page in Pages)
        {
            if (renumberer.PageId(Project.Path, page) is { } id)
            {
                lines.Add("page\t" + id + "\t" + page);
            }
            else
            {
                missing.Add(page);
            }
        }

        if (missing.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                $"Project '{Project.Name}' has no page with exactly these names (letter case counts): {string.Join(", ", missing)}. Nothing was renumbered. Use eplan_page_list to find the exact names.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["missing"] = string.Join("\n", missing) });
        }

        var structures = new HashSet<string>(Pages.Select(PageNames.StructureOf), StringComparer.OrdinalIgnoreCase);
        lines.AddRange(pages.PageNames(Project.Path).Where(name => structures.Contains(PageNames.StructureOf(name))).Select(name => "structure\t" + name));
        return Fingerprint.Of(lines);
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
