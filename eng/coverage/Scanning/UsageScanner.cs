using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Scanning;

/// <summary>Where the add-in uses one EPLAN API member: adapter class, member, file and how.</summary>
internal sealed record UsageSite(string Adapter, string Member, string File, IReadOnlyList<string> Access);

/// <summary>Finds every EPLAN API member the add-in's sources use. One instance per compilation; not thread-safe.</summary>
internal sealed class UsageScanner(Compilation compilation, string repositoryRoot)
{
    public const string AdapterFolder = "src/EplanMcp.Bridge.Eplan/";

    private readonly Dictionary<(string Id, string Adapter, string Member, string File), SortedSet<string>> _sites = [];

    public static bool IsEplan(ISymbol symbol) =>
        (symbol is IArrayTypeSymbol array ? array.ElementType : symbol).ContainingAssembly?.Name.StartsWith("Eplan.EplApi", StringComparison.Ordinal) == true;

    /// <summary>Doc-comment ID to its use sites, both sorted ordinally.</summary>
    public SortedDictionary<string, List<UsageSite>> Scan()
    {
        foreach (INamedTypeSymbol type in SourceTypes(compilation.GlobalNamespace).Where(t => IsAdapter(t.Locations[0])))
        {
            foreach (ISymbol member in type.GetMembers().Where(Walkable))
            {
                AddSites(member, Walk(member));
            }
        }

        return new SortedDictionary<string, List<UsageSite>>(
            _sites.GroupBy(s => s.Key.Id).ToDictionary(g => g.Key, g => g
                .Select(s => new UsageSite(s.Key.Adapter, s.Key.Member, s.Key.File, [.. s.Value]))
                .OrderBy(s => $"{s.File}|{s.Adapter}|{s.Member}", StringComparer.Ordinal).ToList()),
            StringComparer.Ordinal);
    }

    private static IEnumerable<INamedTypeSymbol> SourceTypes(INamespaceOrTypeSymbol container) =>
        container.GetMembers().OfType<INamespaceOrTypeSymbol>().SelectMany(m => m is INamedTypeSymbol type
            ? (type.Locations.Any(l => l.IsInSource) ? SourceTypes(type).Prepend(type) : [])
            : SourceTypes(m));

    private static string TypeName(INamedTypeSymbol type) => type.ContainingType is { } outer ? TypeName(outer) + "." + type.Name : type.Name;

    private static bool Walkable(ISymbol member) => member switch
    {
        IMethodSymbol m => m.MethodKind is not (MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove) && Bodies(m).Any(),
        IPropertySymbol or IEventSymbol or IFieldSymbol => Bodies(member).Any(),
        _ => false,
    };

    /// <summary>The syntax a member's walk covers; a primary constructor (the type declaration) and a field without an initializer have none.</summary>
    private static IEnumerable<SyntaxNode> Bodies(ISymbol member) => member.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).Where(s => s is not TypeDeclarationSyntax and not ParameterSyntax)
        .Select(s => s is VariableDeclaratorSyntax v ? v.Initializer! : s).Where(s => s is not null);

    private bool IsAdapter(Location location) => RelativePath(location.SourceTree!.FilePath) is { } path
        && path.StartsWith(AdapterFolder, StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal);

    private string RelativePath(string path) => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');

    private MemberWalker Walk(ISymbol member)
    {
        var walker = new MemberWalker(compilation.GetSemanticModel(Bodies(member).First().SyntaxTree), IsEplan);
        foreach (SyntaxNode body in Bodies(member))
        {
            walker.Visit(body);
        }

        return walker;
    }

    private void AddSites(ISymbol member, MemberWalker walk)
    {
        string file = RelativePath(member.Locations[0].SourceTree!.FilePath);
        foreach ((string id, string access) in walk.Usages)
        {
            Add(id, TypeName(member.ContainingType), member.Name, file, access);
        }
    }

    private void Add(string id, string adapter, string member, string file, string access)
    {
        if (!_sites.TryGetValue((id, adapter, member, file), out SortedSet<string>? site))
        {
            _sites[(id, adapter, member, file)] = site = new SortedSet<string>(StringComparer.Ordinal);
        }

        site.Add(access);
    }
}
