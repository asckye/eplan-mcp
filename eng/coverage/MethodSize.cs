using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage;

/// <summary>
/// The size gate of the engineering standard's section 4.1 for hand-written code under src/. A method body fails when it
/// is over <see cref="LineLimit"/> lines or over a cyclomatic complexity of <see cref="ComplexityLimit"/>, unless the
/// baseline of that measure lists it, and a listed method may only shrink. Keys carry the project and the type, not the
/// file, so moving a file keeps its entry.
/// </summary>
internal static class MethodSize
{
    public const int LineLimit = 100;
    /// <summary>
    /// The standard names 10 to 15 as the point to look again; the gate stops well above it. Lines alone miss methods
    /// such as DevicePlaceInputs.Check, 106 lines with a complexity of 164 (review 2026-11-17).
    /// </summary>
    public const int ComplexityLimit = 40;

    public static readonly Measure Lines = new("lines", LineLimit, "eng/method-size-baseline.tsv",
        "# Method bodies over 100 lines when the gate was added (2026-11-17). Entries may only shrink or go; never add one.");
    public static readonly Measure Complexity = new("complexity", ComplexityLimit, "eng/method-complexity-baseline.tsv",
        "# Method bodies over a cyclomatic complexity of 40 when the gate was added (2026-11-17). Entries may only shrink or go; never add one.");

    /// <summary>One measured property of a method body, its limit and the baseline file of the methods over it.</summary>
    internal sealed record Measure(string Name, int Limit, string BaselinePath, string Header);

    /// <summary>Every method body over the measure's limit in the hand-written sources below src/, by key.</summary>
    public static SortedDictionary<string, int> Over(string root, Measure measure)
    {
        var sizes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        string source = Path.Combine(root, "src");
        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/Generated/", StringComparison.Ordinal) || relative.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                continue;
            }
            foreach ((string member, int lines, int complexity) in Bodies(File.ReadAllText(file)))
            {
                int value = measure == Lines ? lines : complexity;
                if (value > measure.Limit)
                {
                    Add(sizes, relative[..relative.IndexOf('/', StringComparison.Ordinal)] + "/" + member, value);
                }
            }
        }
        return sizes;
    }

    /// <summary>
    /// Each method, constructor, operator and accessor body of a source text, with its line count between the braces and
    /// its cyclomatic complexity: one, plus one for every branch (if, loop, case, switch arm, conditional, catch) and every
    /// &amp;&amp;, || and ??. Local functions and lambdas count towards the method that holds them.
    /// </summary>
    public static IEnumerable<(string Member, int Lines, int Complexity)> Bodies(string text)
    {
        SyntaxNode tree = CSharpSyntaxTree.ParseText(text).GetRoot();
        foreach (SyntaxNode node in tree.DescendantNodes())
        {
            (string? name, SyntaxNode? body) = node switch
            {
                MethodDeclarationSyntax m => (m.Identifier.Text + "(" + m.ParameterList.Parameters.Count.ToString(CultureInfo.InvariantCulture) + ")", (SyntaxNode?)m.Body ?? m.ExpressionBody),
                ConstructorDeclarationSyntax c => (".ctor(" + c.ParameterList.Parameters.Count.ToString(CultureInfo.InvariantCulture) + ")", (SyntaxNode?)c.Body ?? c.ExpressionBody),
                OperatorDeclarationSyntax o => ("operator " + o.OperatorToken.Text, (SyntaxNode?)o.Body ?? o.ExpressionBody),
                ConversionOperatorDeclarationSyntax o => ("conversion " + o.Type, (SyntaxNode?)o.Body ?? o.ExpressionBody),
                AccessorDeclarationSyntax a when a.Parent?.Parent is PropertyDeclarationSyntax p => (p.Identifier.Text + "." + a.Keyword.Text, (SyntaxNode?)a.Body ?? a.ExpressionBody),
                _ => (null, null),
            };
            if (name is null || body is null)
            {
                continue;
            }
            FileLinePositionSpan span = body.GetLocation().GetLineSpan();
            int lines = body is BlockSyntax ? Math.Max(0, span.EndLinePosition.Line - span.StartLinePosition.Line - 1) : span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            string type = string.Join(".", node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(t => t.Identifier.Text));
            yield return (type + "." + name, lines, 1 + body.DescendantNodes().Count(Branches));
        }
    }

    private static bool Branches(SyntaxNode node) => node is IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
        or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax or ConditionalExpressionSyntax or CatchClauseSyntax
        || node is BinaryExpressionSyntax binary && (binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression) || binary.IsKind(SyntaxKind.CoalesceExpression));

    /// <summary>What the gate refuses: new methods over the limit, listed ones that grew, and entries that are out of date.</summary>
    public static List<string> Problems(SortedDictionary<string, int> measured, SortedDictionary<string, int> baseline, Measure measure)
    {
        var problems = new List<string>();
        string what = measure == Lines ? "lines" : "as its cyclomatic complexity";
        foreach ((string key, int value) in measured)
        {
            if (!baseline.TryGetValue(key, out int allowed))
            {
                problems.Add(Invariant($"{key} has {value} {what}; the limit is {measure.Limit}. Split it into named methods (standard 4.1)."));
            }
            else if (value > allowed)
            {
                problems.Add(Invariant($"{key} grew from {allowed} to {value} ({measure.Name}); a listed method may only shrink."));
            }
        }
        foreach ((string key, int allowed) in baseline)
        {
            if (!measured.TryGetValue(key, out int value) || value < allowed)
            {
                problems.Add(Invariant($"{measure.BaselinePath} is out of date for {key} (listed {allowed}, now {(measured.ContainsKey(key) ? value : 0)}); run: dotnet run --project eng/coverage -c Release -- method-size --update"));
            }
        }
        return problems;
    }

    /// <summary>The problems of both measures on the repository.</summary>
    public static List<string> Problems(string root) =>
        [.. new[] { Lines, Complexity }.SelectMany(measure => Problems(Over(root, measure), ReadBaseline(root, measure), measure))];

    public static SortedDictionary<string, int> ReadBaseline(string root, Measure measure)
    {
        var baseline = new SortedDictionary<string, int>(StringComparer.Ordinal);
        string file = Path.Combine(root, measure.BaselinePath);
        if (!File.Exists(file))
        {
            return baseline;
        }
        foreach (string line in File.ReadAllLines(file).Where(l => l.Length > 0 && l[0] != '#'))
        {
            string[] parts = line.Split('\t');
            baseline[parts[0]] = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        return baseline;
    }

    /// <summary>
    /// Lowers and removes entries of a baseline; adds none, except when the file does not exist yet. A new method over
    /// the limit is never listed by a tool run.
    /// </summary>
    public static int Update(string root, Measure measure)
    {
        SortedDictionary<string, int> measured = Over(root, measure);
        bool first = !File.Exists(Path.Combine(root, measure.BaselinePath));
        SortedDictionary<string, int> baseline = ReadBaseline(root, measure);
        var next = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string key, int value) in measured)
        {
            if (first)
            {
                next[key] = value;
            }
            else if (baseline.TryGetValue(key, out int allowed))
            {
                next[key] = Math.Min(allowed, value);
            }
        }
        File.WriteAllText(Path.Combine(root, measure.BaselinePath), measure.Header + "\n" + string.Concat(next.Select(e => Invariant($"{e.Key}\t{e.Value}\n"))));
        return next.Count;
    }

    // Overloads with the same parameter count share a name; the later ones get an ordinal.
    private static void Add(SortedDictionary<string, int> sizes, string key, int value)
    {
        string unique = key;
        for (int n = 2; sizes.ContainsKey(unique); n++)
        {
            unique = key + "#" + n.ToString(CultureInfo.InvariantCulture);
        }
        sizes[unique] = value;
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
