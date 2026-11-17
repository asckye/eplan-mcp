using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Complete function membership for plan closure; a read inside invoke, not a separate RPC method (ADR-0003).</summary>
public sealed class PlanFunctionsOperation(IProjectCatalog catalog, IPlanFunctionReader reader)
    : Operation<PlanFunctionsArgs, PlanFunctionsData>
{
    public const int MaxFunctions = 10_000;
    public override string Name => OperationNames.PlanFunctions;

    protected override PlanFunctionsData Execute(PlanFunctionsArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Project) || args.Names is null or { Count: < 1 or > 200 }
            || args.Names.Any(string.IsNullOrWhiteSpace) || args.Pages is { Count: < 1 or > 50 }
            || args.Pages?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw InvalidArgument("'project', 1–200 full 'names', and optionally 1–50 'pages' are required; entries cannot be blank.");
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        var names = new HashSet<string>(args.Names, StringComparer.Ordinal);
        var pages = new HashSet<string>(args.Pages ?? [], StringComparer.Ordinal);
        List<PlanFunction> matching = [.. reader.Read(project.Path)
            .Where(f => names.Contains(f.Name) || (f.Page is not null && pages.Contains(f.Page)))
            .OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Page, StringComparer.Ordinal)
            .ThenBy(f => f.Type).ThenBy(f => f.Oid).Take(MaxFunctions + 1)];
        return new()
        {
            Project = project.Name,
            ProjectPath = project.Path,
            Functions = [.. matching.Take(MaxFunctions)],
            Complete = matching.Count <= MaxFunctions,
            Limit = MaxFunctions,
        };
    }
}
