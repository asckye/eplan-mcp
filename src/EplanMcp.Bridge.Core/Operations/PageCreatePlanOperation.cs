using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.PageCreatePlan"/>: checks the pages and that their names are free, and returns one
/// <c>create</c> change per page with the fingerprint (docs/tools/eplan_page_create.md, section 5). Reads only.
/// </summary>
public sealed class PageCreatePlanOperation(IProjectCatalog catalog, IPageReader reader, IPageWriter writer) : Operation<PageCreateArgs, WritePlan>
{
    public override string Name => OperationNames.PageCreatePlan;

    protected override WritePlan Execute(PageCreateArgs args)
    {
        var request = PageCreateRequest.Check(args, catalog, writer);
        IReadOnlyList<string> existing = reader.PageNames(request.Project.Path);
        request.CheckFree(existing);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                .. request.Pages.Select(page => new PlannedChange
                {
                    Action = "create",
                    Kind = "page",
                    Name = page.Name.Text,
                    NewValue = page.Description is null ? page.Type : $"{page.Type}, \"{page.Description}\"",
                }),
            ],
            Fingerprint = request.FingerprintOf(existing),
        };
    }
}
