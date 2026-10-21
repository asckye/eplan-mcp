using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.TerminalStripList"/> (docs/tools/eplan_terminal_strip_list.md). Strip definitions have no
/// page (S21), so the only filter is text in the name; the list is sorted by name, ordinal, so pages stay stable.
/// </summary>
public sealed class TerminalStripListOperation(IProjectCatalog catalog, ITerminalReader terminals)
    : Operation<TerminalStripListArgs, ListData<TerminalStripListItem>>
{
    public override string Name => OperationNames.TerminalStripList;

    protected override ListData<TerminalStripListItem> Execute(TerminalStripListArgs args)
    {
        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        string? text = Given(args.NameContains);
        List<TerminalStripListItem> matching =
        [
            .. terminals.ReadStrips(project.Path)
                .Where(strip => text is null || strip.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(strip => strip.Name, StringComparer.Ordinal),
        ];
        return new ListData<TerminalStripListItem> { Project = project.Name, Items = Paging.Slice(matching, args), Total = matching.Count };
    }
}
