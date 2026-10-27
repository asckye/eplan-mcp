using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads detached drawing evidence for the exact page resolved by page.get; never navigates or writes.</summary>
public sealed class PageDrawingGetOperation(IProjectCatalog catalog, IPageDrawingReader reader) : Operation<PageGetArgs, PageDrawingGeometry>
{
    public override string Name => OperationNames.PageDrawingGet;

    public override object ParseArgs(JToken? args)
    {
        if (args is JObject obj && obj.Properties().FirstOrDefault(p => p.Name is not ("project" or "page" or "language" or "includeProperties")) is { } unknown)
        {
            throw InvalidArgument("/" + unknown.Name.Replace("~", "~0").Replace("/", "~1") + " is unknown; omit this member.");
        }
        return base.ParseArgs(args);
    }

    protected override PageDrawingGeometry Execute(PageGetArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Page))
        {
            throw InvalidArgument("/page must be an exact page name from eplan_page_list.");
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        PageDrawingGeometry result = reader.Read(project.Path, args.Page)
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "Page no longer exists; use eplan_page_list to find it again.");
        result.Project = project.Path;
        return result;
    }
}
