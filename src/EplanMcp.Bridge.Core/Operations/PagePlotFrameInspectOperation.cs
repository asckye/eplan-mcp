using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

public sealed class PagePlotFrameInspectOperation(IProjectCatalog catalog, Func<IPagePlotFrames> frames)
    : Operation<ProjectSnapshotArgs, Dictionary<string, PagePlotFrameState>>
{
    public override string Name => OperationNames.PagePlotFrameInspect;

    protected override Dictionary<string, PagePlotFrameState> Execute(ProjectSnapshotArgs args)
    {
        if (args.Pages is not { Count: >= 1 and <= 50 } || args.Pages.Any(string.IsNullOrWhiteSpace))
        {
            throw InvalidArgument("Supply 1-50 explicit page names for frame inspection.");
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        using IPagePlotFrames session = frames();
        return args.Pages.Distinct(StringComparer.Ordinal).ToDictionary(name => name, name =>
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            return session.Read(project.Path, name);
        }, StringComparer.Ordinal);
    }
}
