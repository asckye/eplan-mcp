using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Independent, bounded readback of page and protected texts; no MCP tool is registered.</summary>
public sealed class PageTextInspectOperation(IProjectCatalog catalog, IPageTextInspector reader) : Operation<ProjectSnapshotArgs, List<PageTextInspection>>
{
    public override string Name => OperationNames.PageTextInspect;

    protected override List<PageTextInspection> Execute(ProjectSnapshotArgs args)
    {
        if (args.Pages is not { Count: >= 1 and <= 50 } || args.OpenReadOnly || args.AfterPage is not null || args.Profile is not null || args.TimeLimitSeconds is not null)
        {
            throw InvalidArgument("page.text.inspect accepts an open project and 1-50 explicit page names only.");
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        return [.. reader.Inspect(project.Path, args.Pages)];
    }
}
