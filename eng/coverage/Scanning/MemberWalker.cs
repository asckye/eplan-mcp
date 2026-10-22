using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Scanning;

/// <summary>The value an operation instance's kind enum has (ExportKind.Pdf), under which only its branches are walked.</summary>
internal sealed record Specialization(ITypeSymbol EnumType, object Value, string Key);

/// <summary>
/// Walks one member's syntax (lambdas and local functions included) and collects the EPLAN members it uses, with how
/// (call, new, get, set, read, write, type, event, action), and the source members it calls. With a
/// <see cref="Specialization"/>, branches that cannot run for that kind value (<c>if (kind == ExportKind.Pdf)</c>,
/// switches on the kind) are skipped; a condition it cannot decide keeps every branch.
/// </summary>
internal sealed class MemberWalker(SemanticModel model, Specialization? kind, Func<ISymbol, bool> isEplan) : CSharpSyntaxWalker
{
    private readonly List<string> _actions = [];
    private readonly List<string> _actionTypes = [];

    public List<(string Id, string Access)> Usages { get; } = [];

    public HashSet<ISymbol> Callees { get; } = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Action IDs: <c>A:name</c> for each action name the member passes as a constant, and <c>A:name/TYPE</c> for each
    /// constant TYPE parameter the member adds to an ActionCallingContext (per member, not per data flow).
    /// </summary>
    public IEnumerable<string> ActionIds =>
        _actions.SelectMany(a => _actionTypes.Select(t => $"A:{a}/{t}").Prepend("A:" + a)).Distinct(StringComparer.Ordinal);

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

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        string?[] texts = [.. node.ArgumentList.Arguments.Select(a => model.GetConstantValue(a.Expression).Value as string)];
        if (Bound(node) is IMethodSymbol method && isEplan(method) && texts is [{ } text, ..])
        {
            switch (method.ContainingType.Name, method.Name)
            {
                case ("ActionManager", "FindAction"):
                    _actions.Add(text);
                    break;
                case ("CommandLineInterpreter", "Execute"):
                    // "name /TYPE:value /OTHER:x", or just the name with the parameters in an ActionCallingContext.
                    string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    _actions.Add(parts[0]);
                    _actionTypes.AddRange(parts.Where(p => p.StartsWith("/TYPE:", StringComparison.OrdinalIgnoreCase)).Select(p => p[6..].Trim('"')));
                    break;
                // AddParameter is declared on Eplan.EplApi.Base.Context, the base class of ActionCallingContext.
                case ("Context" or "ActionCallingContext", "AddParameter") when text.Equals("TYPE", StringComparison.OrdinalIgnoreCase) && texts is [_, { } type]:
                    _actionTypes.Add(type);
                    break;
            }
        }

        base.VisitInvocationExpression(node);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        bool? taken = Evaluate(node.Condition);
        Visit(node.Condition);
        VisitIf(taken != false, node.Statement);
        VisitIf(taken != true, node.Else);
    }

    /// <summary>After <c>if (kind == X) { …; return; }</c> that runs for this kind, the rest of the block does not.</summary>
    public override void VisitBlock(BlockSyntax node)
    {
        foreach (StatementSyntax statement in node.Statements)
        {
            Visit(statement);
            if (statement is IfStatementSyntax branch && Evaluate(branch.Condition) is { } taken
                && (taken ? Exits(branch.Statement) : branch.Else is not null && Exits(branch.Else.Statement)))
            {
                return;
            }
        }
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        bool? taken = Evaluate(node.Condition);
        Visit(node.Condition);
        VisitIf(taken != false, node.WhenTrue);
        VisitIf(taken != true, node.WhenFalse);
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        Visit(node.Expression);
        bool?[] sections = [.. node.Sections.Select(s => s.Labels.Select(l => LabelMatches(node.Expression, l)).Aggregate((bool?)false, Or))];
        bool matched = sections.Contains(true);
        for (int i = 0; i < sections.Length; i++)
        {
            SwitchSectionSyntax section = node.Sections[i];
            foreach (SwitchLabelSyntax label in section.Labels)
            {
                Visit(label);
            }

            if (sections[i] != false || (!matched && section.Labels.Any(l => l is DefaultSwitchLabelSyntax)))
            {
                foreach (StatementSyntax statement in section.Statements)
                {
                    Visit(statement);
                }
            }
        }
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        Visit(node.GoverningExpression);
        foreach (SwitchExpressionArmSyntax arm in node.Arms)
        {
            Visit(arm.Pattern);
            bool? match = Matches(node.GoverningExpression, arm.Pattern);
            match = match == true && arm.WhenClause is not null ? null : match;
            if (match != false)
            {
                Visit(arm.WhenClause);
                Visit(arm.Expression);
            }

            if (match == true)
            {
                return;
            }
        }
    }

    private static bool Exits(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax => true,
        BlockSyntax { Statements: [.., var last] } => Exits(last),
        _ => false,
    };

    private static bool? Not(bool? value) => value is null ? null : !value;

    private static bool? Or(bool? a, bool? b) => a == true || b == true ? true : a is null || b is null ? null : false;

    private static bool? And(bool? a, bool? b) => a == false || b == false ? false : a is null || b is null ? null : true;

    private static string PropertyAccess(ExpressionSyntax node) => (node.Parent is MemberAccessExpressionSyntax m && m.Name == node ? m : node) switch
    {
        { Parent: AssignmentExpressionSyntax a } e when a.Left == e => a.IsKind(SyntaxKind.SimpleAssignmentExpression) ? "set" : "get,set",
        { Parent: PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax } => "get,set",
        { Parent: ArgumentSyntax { RefKindKeyword.RawKind: not 0 } } => "get,set",
        _ => "get",
    };

    private void VisitIf(bool visit, SyntaxNode? node)
    {
        if (visit)
        {
            Visit(node);
        }
    }

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
        else if (symbol.Locations.Any(l => l.IsInSource) && symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol)
        {
            Callees.Add(symbol);
        }
    }

    /// <summary>Whether a case label can match the kind; a default label is decided by the caller.</summary>
    private bool? LabelMatches(ExpressionSyntax governing, SwitchLabelSyntax label) => label switch
    {
        CaseSwitchLabelSyntax c => IsKindValue(governing) ? IsConstantKind(c.Value) : null,
        CasePatternSwitchLabelSyntax p => p.WhenClause is null || Matches(governing, p.Pattern) == false ? Matches(governing, p.Pattern) : null,
        _ => false,
    };

    private bool IsKindValue(ExpressionSyntax expression) =>
        kind is not null && SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(expression).Type, kind.EnumType) && !model.GetConstantValue(expression).HasValue;

    private bool? IsConstantKind(ExpressionSyntax expression) =>
        model.GetConstantValue(expression) is { HasValue: true, Value: { } value } && SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(expression).Type, kind!.EnumType)
            ? value.Equals(kind.Value)
            : null;

    private bool? Matches(ExpressionSyntax governing, PatternSyntax pattern) => !IsKindValue(governing) ? null : pattern switch
    {
        DiscardPatternSyntax => true,
        ConstantPatternSyntax c => IsConstantKind(c.Expression),
        ParenthesizedPatternSyntax p => Matches(governing, p.Pattern),
        UnaryPatternSyntax u => Not(Matches(governing, u.Pattern)),
        BinaryPatternSyntax b when b.IsKind(SyntaxKind.OrPattern) => Or(Matches(governing, b.Left), Matches(governing, b.Right)),
        BinaryPatternSyntax b => And(Matches(governing, b.Left), Matches(governing, b.Right)),
        _ => null,
    };

    private bool? Evaluate(ExpressionSyntax condition) => kind is null ? null : condition switch
    {
        ParenthesizedExpressionSyntax p => Evaluate(p.Expression),
        PrefixUnaryExpressionSyntax u when u.IsKind(SyntaxKind.LogicalNotExpression) => Not(Evaluate(u.Operand)),
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => And(Evaluate(b.Left), Evaluate(b.Right)),
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalOrExpression) => Or(Evaluate(b.Left), Evaluate(b.Right)),
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.EqualsExpression) => Equal(b.Left, b.Right),
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.NotEqualsExpression) => Not(Equal(b.Left, b.Right)),
        IsPatternExpressionSyntax i => Matches(i.Expression, i.Pattern),
        _ => null,
    };

    private bool? Equal(ExpressionSyntax left, ExpressionSyntax right) =>
        IsKindValue(left) ? IsConstantKind(right) : IsKindValue(right) ? IsConstantKind(left) : null;
}
