using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of page.create.plan and page.create.execute. Both build it the same way, so the plan and the
/// execution agree on the pages, and both compute the fingerprint here (docs/tools/eplan_page_create.md, section 5).
/// </summary>
internal sealed class PageCreateRequest
{
    public const int MaxPages = 50;
    public const int MaxDescription = 500;

    /// <summary>The page types engineers edit interactively; the default comes first.</summary>
    public static readonly string[] PageTypes = ["Circuit", "CircuitSingleLine", "Overview", "Graphics"];

    private PageCreateRequest(OpenProject project, List<Page> pages, string? language)
    {
        Project = project;
        Pages = pages;
        Language = language;
    }

    public OpenProject Project { get; }

    public IReadOnlyList<Page> Pages { get; }

    public string? Language { get; }

    /// <summary>Every argument problem at once (mcp-tool-design 6.2) as INVALID_ARGUMENT, then the project.</summary>
    public static PageCreateRequest Check(PageCreateArgs args, IProjectCatalog catalog, IPageWriter writer)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(args.Project))
        {
            violations.Add("'project' is required for writes: a name or path from eplan_project_list.");
        }

        List<PageCreateItem> items = args.Pages ?? [];
        if (items.Count is < 1 or > MaxPages)
        {
            violations.Add($"'pages' must hold 1 to {MaxPages.ToString(CultureInfo.InvariantCulture)} pages.");
        }

        var pages = new List<Page>();
        for (int i = 0; i < items.Count && i < MaxPages; i++)
        {
            string at = "pages[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            PageName? name = PageNames.Parse(items[i].Name, out string? problem);
            string? type = string.IsNullOrEmpty(items[i].PageType) ? PageTypes[0] : PageTypes.FirstOrDefault(t => string.Equals(t, items[i].PageType, StringComparison.OrdinalIgnoreCase));
            string? description = string.IsNullOrEmpty(items[i].Description) ? null : items[i].Description;
            int same = pages.FindIndex(p => string.Equals(p.Name.Text, items[i].Name, StringComparison.OrdinalIgnoreCase));
            AddIf(violations, problem is not null, $"{at}.name {problem}");
            AddIf(violations, same >= 0, $"{at}.name repeats pages[{same.ToString(CultureInfo.InvariantCulture)}].name (letter case does not count).");
            AddIf(violations, type is null, $"{at}.page_type must be one of {string.Join(", ", PageTypes)}.");
            AddIf(violations, description?.Length > MaxDescription, $"{at}.description must have at most {MaxDescription.ToString(CultureInfo.InvariantCulture)} characters.");
            pages.Add(new Page(name ?? new PageName(items[i].Name ?? string.Empty, null, null, null, 0), type ?? PageTypes[0], description));
        }

        string? language = string.IsNullOrWhiteSpace(args.Language) ? null : args.Language!.Trim();
        AddIf(violations, language is not null && !writer.KnowsLanguage(language), "'language' is not a language EPLAN knows; use a code like zh_CN or en_US, or omit it for EPLAN's user interface language.");
        if (violations.Count > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "Invalid arguments: " + string.Join(" ", violations),
                retryable: false,
                new Dictionary<string, string> { ["violations"] = string.Join("\n", violations) });
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        return project.ReadOnly
            ? throw new BridgeErrorException(ErrorReason.InvalidArgument, "EPLAN has this project open read-only, so it cannot be changed. Ask the engineer to open it for writing.")
            : new PageCreateRequest(project, pages, language);
    }

    /// <summary>
    /// The plan's state: the existing pages in each structure a new page goes into (the name before its '/', letter
    /// case not counted), sorted. A page added or removed there between the plan and the execution changes it.
    /// </summary>
    public string FingerprintOf(IReadOnlyList<string> existing)
    {
        var structures = new HashSet<string>(Pages.Select(p => p.Name.Structure), StringComparer.OrdinalIgnoreCase);
        return Fingerprint.Of(existing.Where(name => structures.Contains(PageNames.StructureOf(name))));
    }

    /// <summary>ALREADY_EXISTS when a page has one of the names already, ignoring letter case.</summary>
    public void CheckFree(IReadOnlyList<string> existing)
    {
        var wanted = new HashSet<string>(Pages.Select(p => p.Name.Text), StringComparer.OrdinalIgnoreCase);
        string[] taken = [.. existing.Where(wanted.Contains).Distinct(StringComparer.Ordinal)];
        if (taken.Length > 0)
        {
            throw new BridgeErrorException(
                ErrorReason.AlreadyExists,
                "Pages with these names exist already (letter case does not count), so nothing was created. Choose unused page numbers; eplan_page_list with name_prefix shows the numbers in use.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = Project.Name, ["existing"] = string.Join("\n", taken.Take(ProjectResolver.MaxCandidates)) });
        }
    }

    private static void AddIf(List<string> violations, bool condition, string violation)
    {
        if (condition)
        {
            violations.Add(violation);
        }
    }

    /// <summary>One page to create, checked.</summary>
    internal sealed class Page(PageName name, string type, string? description)
    {
        public PageName Name { get; } = name;

        public string Type { get; } = type;

        public string? Description { get; } = description;
    }
}
