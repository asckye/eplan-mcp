using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.LayoutGet"/> (docs/tools/eplan_layout_get.md). Enclosures come first so the surfaces a caller
/// places onto are on the first page; a device counts as placed when a 3D function carries its tag (A136: the layout
/// tools name the component after the device).
/// </summary>
public sealed class LayoutGetOperation(IProjectCatalog catalog, ILayoutReader layout) : Operation<LayoutGetArgs, LayoutGetData>
{
    /// <summary>The most unplaced devices returned; the total is always reported.</summary>
    public const int MaxUnplaced = 200;

    public override string Name => OperationNames.LayoutGet;

    protected override LayoutGetData Execute(LayoutGetArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        LayoutRecord record = layout.Read(project.Path);
        string? space = Given(args.Space)?.TrimStart('+');
        List<LayoutSpace> spaces = [.. record.Spaces.Where(s => space is null || string.Equals(s.Designation, space, StringComparison.Ordinal))];
        if (space is not null && spaces.Count == 0)
        {
            throw new BridgeErrorException(
                ErrorReason.InvalidArgument,
                "The project has no installation space with this designation; omit 'space' to list them.",
                retryable: false,
                new Dictionary<string, string> { ["space"] = space });
        }

        LayoutObject[] objects = [.. record.Objects
            .Where(o => space is null || string.Equals(o.Space, space, StringComparison.Ordinal))
            .Select((o, i) => (Object: o, Index: i))
            .OrderBy(p => p.Object.Kind == "Cabinet" ? 0 : 1).ThenBy(p => p.Index)
            .Select(p => p.Object)];
        LayoutDevice[] unplaced = [.. record.Devices.Where(d => !record.Names3D.Contains(d.Tag)).GroupBy(d => d.Tag, StringComparer.Ordinal).Select(g => g.First())];
        return new LayoutGetData
        {
            Project = project.Name,
            Spaces = spaces,
            Objects = [.. Paging.Slice(objects, args)],
            Total = objects.Length,
            Unplaced = [.. unplaced.Take(MaxUnplaced)],
            UnplacedTotal = unplaced.Length,
        };
    }
}
