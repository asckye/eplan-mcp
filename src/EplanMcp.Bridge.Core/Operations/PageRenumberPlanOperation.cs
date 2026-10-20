using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageRenumberPlan"/>: finds the pages by their exact names and returns one change per page,
/// whose new name only EPLAN's rules decide (M4 design Q7), with the numbering in the details and the fingerprint
/// (docs/tools/eplan_page_renumber.md, section 5). Reads only.
/// </summary>
public sealed class PageRenumberPlanOperation(IProjectCatalog catalog, IPageReader pages, IPageRenumberer renumberer) : Operation<PageRenumberArgs, WritePlan>
{
    public override string Name => OperationNames.PageRenumberPlan;

    protected override WritePlan Execute(PageRenumberArgs args)
    {
        PageRenumberRequest request = PageRenumberRequest.Check(args, catalog);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Pages.Select(page => new PlannedChange { Action = "update", Kind = "page", Name = page, Property = "page number" })],
            Fingerprint = request.FingerprintOf(renumberer, pages),
            Details = new Dictionary<string, string>
            {
                ["numbering"] = request.Numbering,
                ["start"] = request.Start.ToString(CultureInfo.InvariantCulture),
                ["step"] = request.Step.ToString(CultureInfo.InvariantCulture),
                ["keepText"] = request.KeepText ? "true" : "false",
                ["subpages"] = request.Subpages,
            },
        };
    }
}
