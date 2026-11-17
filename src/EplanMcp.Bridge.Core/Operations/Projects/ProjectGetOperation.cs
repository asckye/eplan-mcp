using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectGet"/>: resolves the project and reads its overview in the same main-thread call
/// (M2 design 4.7). Groups the pages by type here, where it can be tested without EPLAN.
/// </summary>
public sealed class ProjectGetOperation(IProjectCatalog catalog, IProjectReader reader) : Operation<ProjectGetArgs, ProjectGetData>
{
    public override string Name => OperationNames.ProjectGet;

    protected override ProjectGetData Execute(ProjectGetArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        ProjectContent content = reader.Read(project.Path, args.IncludeProperties, args.Language);
        return new ProjectGetData
        {
            Project = project,
            Pages = content.PageTypes.Count,
            Devices = content.Devices,
            Connections = content.Connections,
            PartReferences = content.PartReferences,
            PagesByType = CountByType(content.PageTypes),
            Properties = args.IncludeProperties ? content.Properties ?? [] : null,
        };
    }

    /// <summary>Most frequent first; equal counts by name, so the order does not depend on the page order.</summary>
    internal static List<PageTypeCount> CountByType(IEnumerable<string> pageTypes) =>
    [
        .. pageTypes
            .GroupBy(type => type, StringComparer.Ordinal)
            .Select(group => new PageTypeCount { Type = group.Key, Count = group.Count() })
            .OrderByDescending(count => count.Count)
            .ThenBy(count => count.Type, StringComparer.Ordinal),
    ];
}
