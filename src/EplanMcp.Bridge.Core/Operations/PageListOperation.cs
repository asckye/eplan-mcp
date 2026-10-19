using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageList"/>: resolves the project, lets EPLAN filter by name prefix and page type, filters
/// the descriptions by text here and returns one slice, all in one main-thread call (M2 design 4.3, 4.7).
/// </summary>
public sealed class PageListOperation(IProjectCatalog catalog, IPageReader reader) : Operation<PageListArgs, ListData<PageSummary>>
{
    /// <summary>
    /// Named when <c>page_type</c> is unknown. EPLAN has 75 page types, too many for the tool's schema enum
    /// (tool spec 2); eplan_tool_usage lists them all.
    /// </summary>
    public static IReadOnlyList<string> CommonPageTypes { get; } =
    [
        "Circuit", "CircuitSingleLine", "Overview", "TitlePage", "TableOfContents", "TerminalDiagram", "TerminalConnectiondiagram",
        "CableOverview", "CableConnectionDiagram", "DeviceList", "PartsList", "PLCDiagram", "PanelLayout", "Graphics",
    ];

    public override string Name => OperationNames.PageList;

    protected override ListData<PageSummary> Execute(PageListArgs args)
    {
        string? pageType = CanonicalPageType(args.PageType, reader.PageTypes);
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        IReadOnlyList<PageSummary> pages = reader.ListPages(project.Path, Optional(args.NamePrefix), pageType, args.Language);
        List<PageSummary> matching = Optional(args.Text) is { } text
            ? [.. pages.Where(page => page.Description is not null && page.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)]
            : [.. pages];
        return new ListData<PageSummary> { Project = project.Name, Items = Paging.Slice(matching, args), Total = matching.Count };
    }

    /// <summary>EPLAN's spelling of <paramref name="requested"/>, matched ignoring case; models write "circuit" too.</summary>
    internal static string? CanonicalPageType(string? requested, IReadOnlyCollection<string> known)
    {
        if (Optional(requested) is not { } wanted)
        {
            return null;
        }

        string? match = known.FirstOrDefault(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        string violation = $"'page_type' must be an EPLAN page type such as {string.Join(", ", CommonPageTypes)}; " +
            "eplan_tool_usage for eplan_page_list (topic 'fields') lists all of them.";
        throw new BridgeErrorException(
            ErrorReason.InvalidArgument,
            "Invalid arguments: " + violation,
            retryable: false,
            new Dictionary<string, string> { ["violations"] = violation });
    }

    private static string? Optional(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
