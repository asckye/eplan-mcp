namespace EplanMcp.Coverage.Reporting;

/// <summary>Manually recorded installation totals unavailable from the XML-backed catalog.</summary>
internal sealed record ApiTotals(string EplanBuild, int PublicElements, int Assemblies,
    UndocumentedAssemblies UndocumentedAssemblies, int PrivateDmClassIdEnums, ActionTotals Actions)
{
    internal const string RelativePath = "catalog/mapping/api-totals.json";

    internal static ApiTotals Load(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, RelativePath);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{RelativePath} is missing; installation totals are required to generate coverage.");
        }

        return CoverageJson.Read<ApiTotals>(path);
    }
}

internal sealed record UndocumentedAssemblies(IReadOnlyList<string> Names, int Types, int Methods, int Properties);

internal sealed record ActionTotals(int Registered, int ActionsSection, int Interactions, int AsmActions, int FoundByName, string FoundVariant);
