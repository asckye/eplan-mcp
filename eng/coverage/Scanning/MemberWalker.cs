using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Scanning;

/// <summary>
/// Walks one member's syntax (lambdas and local functions included) and collects the EPLAN members it uses, with how
/// (call, new, get, set, read, write, type, event).
/// </summary>
internal sealed class MemberWalker(SemanticModel model, Func<ISymbol, bool> isEplan) : CSharpSyntaxWalker
{
    public List<(string Id, string Access)> Usages { get; } = [];

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        Record(node, Bound(node));
        base.VisitIdentifierName(node);
    }

    public override void VisitGenericName(GenericNameSyntax node)
    {
        Record(node, Bound(node));
        base.VisitGenericName(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        Use(Bound(node), "new");
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        Use(Bound(node), "new");
        base.VisitImplicitObjectCreationExpression(node);
    }

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        Record(node, Bound(node));
        base.VisitElementAccessExpression(node);
    }

    private static string PropertyAccess(ExpressionSyntax node) => (node.Parent is MemberAccessExpressionSyntax m && m.Name == node ? m : node) switch
    {
        { Parent: AssignmentExpressionSyntax a } e when a.Left == e => a.IsKind(SyntaxKind.SimpleAssignmentExpression) ? "set" : "get,set",
        { Parent: PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax } => "get,set",
        { Parent: ArgumentSyntax { RefKindKeyword.RawKind: not 0 } } => "get,set",
        _ => "get",
    };

    private ISymbol? Bound(SyntaxNode node) => model.GetSymbolInfo(node) is { Symbol: { } s } ? s : model.GetSymbolInfo(node).CandidateSymbols is [var only] ? only : null;

    private void Record(ExpressionSyntax node, ISymbol? symbol)
    {
        switch (symbol)
        {
            case IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction }:
                return; // walked as part of this member
            case IMethodSymbol method:
                Use(method, "call");
                break;
            case IPropertySymbol property:
                Use(property, PropertyAccess(node));
                break;
            case IFieldSymbol field:
                Use(field, PropertyAccess(node).Replace("get", "read", StringComparison.Ordinal).Replace("set", "write", StringComparison.Ordinal));
                break;
            case IEventSymbol:
                Use(symbol, "event");
                break;
            case INamedTypeSymbol type:
                Use(type, "type");
                break;
        }
    }

    private void Use(ISymbol? symbol, string access)
    {
        symbol = symbol switch
        {
            IMethodSymbol { ReducedFrom: { } extension } => extension,
            IMethodSymbol { AssociatedSymbol: IPropertySymbol or IEventSymbol } accessor => accessor.AssociatedSymbol,
            _ => symbol,
        };
        symbol = symbol?.OriginalDefinition;
        if (symbol is null)
        {
            return;
        }

        if (isEplan(symbol))
        {
            string id = symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString();
            Usages.AddRange(access.Split(',').Select(a => (id, a)));
        }
    }
}
