using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only symbol discovery, sorting and paging (api-notes A52–A55).</summary>
public sealed class SymbolSearchOperation(IProjectCatalog catalog, ISymbolReader reader) : Operation<SymbolSearchArgs, ListData<SymbolSearchItem>>
{
    public override string Name => OperationNames.SymbolSearch;

    public override object ParseArgs(JToken? args) => args is JObject obj && obj.Properties().Any(p => p.Name is not ("project" or "library" or "nameContains" or "kind" or "limit" or "offset"))
        ? throw InvalidArgument("Unknown symbol search member; use project, library, nameContains, kind, limit and offset.") : base.ParseArgs(args);

    protected override ListData<SymbolSearchItem> Execute(SymbolSearchArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Project) || (args.Kind is not null && !SymbolKinds.All.Contains(args.Kind)) || args.Limit is < 1 or > 200 || args.Offset < 0)
        {
            throw InvalidArgument("Specify 'project', a known 'kind', limit 1-200 and a nonnegative offset.");
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        List<SymbolSearchItem> items = [.. reader.Read(project.Path, args).OrderBy(s => s.Library, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).ThenBy(s => s.Variant)];
        if (items.SelectMany(s => s.ConnectionPoints).Any(p => p.Location.Length != 2 || p.Location.Any(v => double.IsNaN(v) || double.IsInfinity(v))))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "A symbol has invalid connection point geometry. Narrow the filters to another symbol.");
        }

        return new ListData<SymbolSearchItem> { Project = project.Name, Items = Paging.Slice(items, args), Total = items.Count };
    }
}
