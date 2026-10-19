using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageGet"/>: resolves the project, finds the page by its full name and lists the devices on
/// it, all in one main-thread call (M2 design 4.7).
/// </summary>
public sealed class PageGetOperation(IProjectCatalog catalog, IPageReader reader) : Operation<PageGetArgs, PageGetData>
{
    public override string Name => OperationNames.PageGet;

    protected override PageGetData Execute(PageGetArgs args)
    {
        if (string.IsNullOrEmpty(args.Page))
        {
            const string violation = "'page' is required: a full page name such as =GAA+A1&EFS1/1, as eplan_page_list returns it.";
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "Invalid arguments: " + violation,
                retryable: false,
                new Dictionary<string, string> { ["violations"] = violation });
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        PageContent content = Find(project, args);
        return new PageGetData
        {
            Project = project.Name,
            Page = content.Page,
            Devices =
            [
                .. content.Functions
                    .Where(function => DeviceTags.IsDevice(function.IsMain, function.Name))
                    .Select(function => new PageDevice { Tag = function.Name, VisibleTag = DeviceTags.Shown(function.VisibleName), Category = function.Category }),
            ],
            Properties = args.IncludeProperties ? content.Properties ?? [] : null,
        };
    }

    /// <summary>
    /// EPLAN's name filter is case-sensitive (S6), while engineers and models often change the case, so a miss is
    /// retried ignoring case and taken only when one page matches (tool spec 5).
    /// </summary>
    private PageContent Find(OpenProject project, PageGetArgs args)
    {
        PageContent? exact = reader.ReadPage(project.Path, args.Page, args.IncludeProperties, args.Language);
        if (exact is not null)
        {
            return exact;
        }

        string[] matches =
        [
            .. reader.PageNames(project.Path)
                .Where(name => string.Equals(name, args.Page, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal),
        ];
        if (matches.Length > 1)
        {
            throw ProjectResolver.Ambiguous(
                "page",
                args.Page,
                matches,
                "Several pages have this name in different letter case; pass one of the candidates exactly as 'page'.");
        }

        return (matches.Length == 1 ? reader.ReadPage(project.Path, matches[0], args.IncludeProperties, args.Language) : null)
            ?? throw new BridgeErrorException(
                ErrorReason.PageNotFound,
                "No page has this name. Use eplan_page_list with name_prefix to find the exact page name.",
                retryable: false,
                new Dictionary<string, string> { ["project"] = project.Name, ["page"] = args.Page });
    }
}
