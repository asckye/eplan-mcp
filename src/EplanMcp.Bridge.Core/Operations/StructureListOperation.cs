using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary><see cref="OperationNames.StructureList"/> (docs/tools/eplan_structure_list.md), in EPLAN's order.</summary>
public sealed class StructureListOperation(IProjectCatalog catalog, IStructureIdentifiers identifiers)
    : Operation<StructureListArgs, ListData<StructureIdentifier>>
{
    public override string Name => OperationNames.StructureList;

    protected override ListData<StructureIdentifier> Execute(StructureListArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        List<StructureIdentifier> matching =
        [
            .. identifiers.Read(project.Path)
                .Where(i => args.Block is null || StructureNames.Prefix(i.Name) == args.Block)
                .Where(i => !args.UnusedOnly || !i.Used),
        ];
        return new ListData<StructureIdentifier> { Project = project.Name, Items = Paging.Slice(matching, args), Total = matching.Count };
    }
}
