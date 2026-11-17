using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads detached drawing evidence for the exact page resolved by page.get; never navigates or writes.</summary>
public sealed class PageDrawingGetOperation(IProjectCatalog catalog, IPageDrawingReader reader) : Operation<PageGetArgs, PageDrawingGeometry>
{
    public override string Name => OperationNames.PageDrawingGet;

    public override object ParseArgs(JToken? args)
    {
        if (args is JObject obj && obj.Properties().FirstOrDefault(p => p.Name is not ("project" or "page" or "language" or "includeProperties" or "drawingOffset" or "drawingLimit")) is { } unknown)
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
        if (args.DrawingLimit is int limit)
        {
            if (limit is < 1 or > 200 || args.DrawingOffset is < 0)
            { throw InvalidArgument("/drawingLimit must be 1..200 and /drawingOffset at least 0."); }
            // Parents first, as eplan_page_get lists them (agent eval 2026-11-06), then the requested slice, in a new result:
            // the reader's geometry is left as it was read.
            return new PageDrawingGeometry
            {
                Project = result.Project,
                Page = result.Page,
                GridSize = result.GridSize,
                PageSize = result.PageSize,
                PlotFrameSize = result.PlotFrameSize,
                DrawingArea = result.DrawingArea,
                ColumnEdges = result.ColumnEdges,
                PlacementsFingerprint = result.Fingerprint(),
                PlacementTotal = result.Placements.Count,
                Placements = [.. result.Placements.OrderBy(p => p.Parent is null ? 0 : 1).Skip(args.DrawingOffset ?? 0).Take(limit)],
            };
        }
        return result;
    }
}
