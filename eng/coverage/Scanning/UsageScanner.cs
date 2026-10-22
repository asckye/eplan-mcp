using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Scanning;

/// <summary>Where the add-in uses one EPLAN API member: adapter class, member, file, how, and for which operations.</summary>
internal sealed record UsageSite(string Adapter, string Member, string File, IReadOnlyList<string> Access, IReadOnlyList<string> Operations);

/// <summary>
/// Finds every EPLAN API member the add-in's sources use and attributes each use to the Bridge operations that reach
/// it: from an operation class's Execute through Bridge.Core, into the adapters that implement the interfaces it calls,
/// and on through the adapters' own calls. A use no operation reaches (add-in start, dispatcher, exception mapping) has
/// no operations. One instance per compilation; not thread-safe.
/// </summary>
internal sealed class UsageScanner(Compilation compilation, string repositoryRoot)
{
    public const string AdapterFolder = "src/EplanMcp.Bridge.Eplan/";

    private readonly Dictionary<string, Dictionary<ISymbol, MemberWalker>> _walks = [];
    private readonly Dictionary<(string Id, string Adapter, string Member, string File), (SortedSet<string> Access, SortedSet<string> Operations)> _sites = [];
    private Dictionary<ISymbol, List<ISymbol>>? _implementations;

    public static bool IsEplan(ISymbol symbol) =>
        (symbol is IArrayTypeSymbol array ? array.ElementType : symbol).ContainingAssembly?.Name.StartsWith("Eplan.EplApi", StringComparison.Ordinal) == true;

    /// <summary>Doc-comment ID (or A: ID) to its use sites, both sorted ordinally.</summary>
    public SortedDictionary<string, List<UsageSite>> Scan(IEnumerable<OperationRoot> operations)
    {
        foreach (INamedTypeSymbol type in SourceTypes(compilation.GlobalNamespace).Where(t => IsAdapter(t.Locations[0])))
        {
            AddDeclarationSites(type);
            foreach (ISymbol member in type.GetMembers().Where(Walkable))
            {
                AddSites(member, Walk(member, null), null);
            }
        }

        foreach (OperationRoot operation in operations)
        {
            INamedTypeSymbol type = compilation.GetTypeByMetadataName(operation.ClassName)
                ?? throw new InvalidOperationException($"Operation class {operation.ClassName} is not in the compilation.");
            Specialization? kind = operation.KindType is null ? null : Kind(operation);
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var queue = new Queue<ISymbol>(type.GetMembers().Where(m => m is IMethodSymbol { Name: "Execute" or "ParseArgs" } or IMethodSymbol { MethodKind: MethodKind.Constructor }));
            while (queue.TryDequeue(out ISymbol? member))
            {
                if (visited.Add(member) && Walkable(member))
                {
                    MemberWalker walk = Walk(member, kind);
                    AddSites(member, walk, operation.Name);
                    foreach (ISymbol callee in walk.Callees.SelectMany(Implementations))
                    {
                        queue.Enqueue(callee);
                    }
                }
            }
        }

        return new SortedDictionary<string, List<UsageSite>>(
            _sites.GroupBy(s => s.Key.Id).ToDictionary(g => g.Key, g => g
                .Select(s => new UsageSite(s.Key.Adapter, s.Key.Member, s.Key.File, [.. s.Value.Access], [.. s.Value.Operations]))
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

    private Specialization Kind(OperationRoot operation)
    {
        INamedTypeSymbol enumType = compilation.GetTypeByMetadataName(operation.KindType!) ?? throw new InvalidOperationException($"Enum {operation.KindType} not found.");
        IFieldSymbol value = enumType.GetMembers(operation.KindValue!).OfType<IFieldSymbol>().Single();
        return new Specialization(enumType, value.ConstantValue!, operation.KindType + "." + operation.KindValue);
    }

    /// <summary>One walk per member and kind value; an operation without a kind shares the plain walk.</summary>
    private MemberWalker Walk(ISymbol member, Specialization? kind)
    {
        string key = kind?.Key ?? string.Empty;
        Dictionary<ISymbol, MemberWalker> walks = _walks.TryGetValue(key, out var found) ? found : _walks[key] = new(SymbolEqualityComparer.Default);
        if (!walks.TryGetValue(member, out MemberWalker? walker))
        {
            walks[member] = walker = new MemberWalker(compilation.GetSemanticModel(Bodies(member).First().SyntaxTree), kind, IsEplan);
            foreach (SyntaxNode body in Bodies(member))
            {
                walker.Visit(body);
            }
        }

        return walker;
    }

    /// <summary>An interface member stands for every source implementation of it (the adapters behind a Bridge.Core interface).</summary>
    private IEnumerable<ISymbol> Implementations(ISymbol member)
    {
        if (member.ContainingType.TypeKind != TypeKind.Interface)
        {
            return [member];
        }

        _implementations ??= SourceTypes(compilation.GlobalNamespace)
            .SelectMany(t => t.AllInterfaces.SelectMany(i => i.GetMembers()).Select(m => (Interface: m.OriginalDefinition, Implementation: t.FindImplementationForInterfaceMember(m))))
            .Where(p => p.Implementation is not null && p.Implementation.Locations.Any(l => l.IsInSource))
            .GroupBy(p => p.Interface, SymbolEqualityComparer.Default)
            .ToDictionary(g => g.Key!, g => g.Select(p => p.Implementation!).ToList(), SymbolEqualityComparer.Default);
        return _implementations.TryGetValue(member, out List<ISymbol>? implementations) ? implementations : [];
    }

    private void AddSites(ISymbol member, MemberWalker walk, string? operation)
    {
        if (IsAdapter(member.Locations[0]))
        {
            string file = RelativePath(member.Locations[0].SourceTree!.FilePath);
            foreach ((string id, string access) in walk.Usages.Concat(walk.ActionIds.Select(a => (a, "action"))))
            {
                Add(id, TypeName(member.ContainingType), member.Name, file, access, operation);
            }
        }
    }

    /// <summary>EPLAN base types and interfaces of an adapter, and the EPLAN interface members it implements or overrides.</summary>
    private void AddDeclarationSites(INamedTypeSymbol type)
    {
        string file = RelativePath(type.Locations[0].SourceTree!.FilePath);
        foreach (INamedTypeSymbol baseType in type.Interfaces.Concat(type.BaseType is { } b ? [b] : []).Where(IsEplan))
        {
            Add(baseType.OriginalDefinition.GetDocumentationCommentId()!, TypeName(type), "(base)", file, "type", null);
        }

        foreach (ISymbol member in type.AllInterfaces.Where(IsEplan).SelectMany(i => i.GetMembers()))
        {
            if (type.FindImplementationForInterfaceMember(member) is { } implementation && SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
            {
                Add(member.OriginalDefinition.GetDocumentationCommentId()!, TypeName(type), implementation.Name, file, "implements", null);
            }
        }

        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.OverriddenMethod is { } o && IsEplan(o)))
        {
            Add(method.OverriddenMethod!.OriginalDefinition.GetDocumentationCommentId()!, TypeName(type), method.Name, file, "overrides", null);
        }
    }

    /// <summary>The plain walk records how a member is used; an operation's walk records that the operation reaches it.</summary>
    private void Add(string id, string adapter, string member, string file, string access, string? operation)
    {
        if (!_sites.TryGetValue((id, adapter, member, file), out var site))
        {
            _sites[(id, adapter, member, file)] = site = (new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
        }

        (operation is null ? site.Access : site.Operations).Add(operation ?? access);
    }
}
