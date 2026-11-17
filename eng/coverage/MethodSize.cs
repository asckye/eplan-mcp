using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage;

/// <summary>
/// The size gate of the engineering standard's section 4.1 for hand-written code under src/: a method body over
/// <see cref="Limit"/> lines fails unless eng/method-size-baseline.tsv lists it, and a listed method may only shrink.
/// Keys carry the project and the type, not the file, so moving a file keeps its entry.
/// </summary>
internal static class MethodSize
{
    public const int Limit = 100;
    public const string BaselinePath = "eng/method-size-baseline.tsv";
    private const string Header = "# Method bodies over 100 lines when the gate was added (2026-11-17). Entries may only shrink or go; never add one.";

    /// <summary>Every method body over the limit in the hand-written sources below src/, by key.</summary>
    public static SortedDictionary<string, int> Measure(string root)
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
            foreach ((string member, int lines) in Bodies(File.ReadAllText(file)))
            {
                if (lines > Limit)
                {
                    Add(sizes, relative[..relative.IndexOf('/', StringComparison.Ordinal)] + "/" + member, lines);
                }
            }
        }
        return sizes;
    }

    /// <summary>Each method, constructor, operator and accessor body of a source text with its line count between the braces.</summary>
    public static IEnumerable<(string Member, int Lines)> Bodies(string text)
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
            yield return (type + "." + name, lines);
        }
    }

    /// <summary>What the gate refuses: new long methods, listed ones that grew, and entries that are out of date.</summary>
    public static List<string> Problems(SortedDictionary<string, int> measured, SortedDictionary<string, int> baseline)
    {
        var problems = new List<string>();
        foreach ((string key, int lines) in measured)
        {
            if (!baseline.TryGetValue(key, out int allowed))
            {
                problems.Add(Invariant($"{key} has {lines} lines; the limit is {Limit}. Split it into named methods (standard 4.1)."));
            }
            else if (lines > allowed)
            {
                problems.Add(Invariant($"{key} grew from {allowed} to {lines} lines; a listed method may only shrink."));
            }
        }
        foreach ((string key, int allowed) in baseline)
        {
            if (!measured.TryGetValue(key, out int lines) || lines < allowed)
            {
                problems.Add(Invariant($"{BaselinePath} is out of date for {key} (listed {allowed}, now {(measured.ContainsKey(key) ? lines : 0)}); run: dotnet run --project eng/coverage -c Release -- method-size --update"));
            }
        }
        return problems;
    }

    public static SortedDictionary<string, int> ReadBaseline(string root)
    {
        var baseline = new SortedDictionary<string, int>(StringComparer.Ordinal);
        string file = Path.Combine(root, BaselinePath);
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
    /// Lowers and removes entries; adds none, except when the file does not exist yet. A new long method is never listed
    /// by a tool run.
    /// </summary>
    public static int Update(string root)
    {
        SortedDictionary<string, int> measured = Measure(root);
        bool first = !File.Exists(Path.Combine(root, BaselinePath));
        SortedDictionary<string, int> baseline = ReadBaseline(root);
        var next = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string key, int lines) in measured)
        {
            if (first)
            {
                next[key] = lines;
            }
            else if (baseline.TryGetValue(key, out int allowed))
            {
                next[key] = Math.Min(allowed, lines);
            }
        }
        File.WriteAllText(Path.Combine(root, BaselinePath), Header + "\n" + string.Concat(next.Select(e => Invariant($"{e.Key}\t{e.Value}\n"))));
        return next.Count;
    }

    // Overloads with the same parameter count share a name; the later ones get an ordinal.
    private static void Add(SortedDictionary<string, int> sizes, string key, int lines)
    {
        string unique = key;
        for (int n = 2; sizes.ContainsKey(unique); n++)
        {
            unique = key + "#" + n.ToString(CultureInfo.InvariantCulture);
        }
        sizes[unique] = lines;
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
