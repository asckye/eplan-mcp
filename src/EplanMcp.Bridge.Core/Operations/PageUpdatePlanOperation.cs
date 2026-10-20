using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageUpdatePlan"/>: finds the pages and returns one <c>update</c> change per page whose
/// description in the language differs, with the fingerprint (docs/tools/eplan_page_update.md, section 5). Reads only.
/// </summary>
public sealed class PageUpdatePlanOperation(IProjectCatalog catalog, IPageReader reader, IPageDescriptions descriptions) : Operation<PageUpdateArgs, WritePlan>
{
    public override string Name => OperationNames.PageUpdatePlan;

    protected override WritePlan Execute(PageUpdateArgs args)
    {
        var request = PageUpdateRequest.Check(args, catalog, reader, descriptions);
        List<(PageUpdateRequest.Page Page, PageDescription Before)> pages = [.. request.Pages.Select(p => (p, descriptions.Read(request.Project.Path, p.Name)))];
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                .. pages.Where(p => Changes(p, request.Language)).Select(p => new PlannedChange
                {
                    Action = "update",
                    Kind = "page",
                    Name = p.Page.Name,
                    Property = $"description ({request.Language})",
                    OldValue = p.Before.TextIn(request.Language),
                    NewValue = p.Page.Text,
                }),
            ],
            Fingerprint = PageUpdateRequest.FingerprintOf(pages),
        };
    }

    /// <summary>Whether the page's text in the language is not already the requested one.</summary>
    internal static bool Changes((PageUpdateRequest.Page Page, PageDescription Before) page, string language) =>
        !string.Equals(page.Before.TextIn(language), page.Page.Text, StringComparison.Ordinal);
}
