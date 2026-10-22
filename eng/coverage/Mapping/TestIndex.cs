using EplanMcp.Coverage.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Mapping;

/// <summary>A test class with its size trait (Small, Medium, Large) and file.</summary>
internal sealed record TestClass(string Name, string Size, string File);

/// <summary>
/// The test classes under tests/ that carry <c>[Trait("Size", …)]</c>, each with the words it uses: string literals,
/// identifiers and <c>Type.Member</c> accesses. A tool's tests are the classes that name the tool or its Host method or
/// constant; an operation's tests are those that name its class. Syntax only, so it runs without building anything.
/// </summary>
internal sealed class TestIndex
{
    private readonly List<(TestClass Class, HashSet<string> Words)> _classes = [];

    public TestIndex(string repositoryRoot)
    {
        // This tool's own tests name tools as fixture data; they are not evidence for them.
        foreach (string file in BridgeCompilation.SourceFiles(Path.Combine(repositoryRoot, "tests"))
            .Where(f => !Path.GetRelativePath(repositoryRoot, f).Replace('\\', '/').StartsWith("tests/EplanMcp.Coverage.Tests/", StringComparison.Ordinal)))
        {
            foreach (ClassDeclarationSyntax type in CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                string? size = type.AttributeLists.SelectMany(l => l.Attributes)
                    .Where(a => a.Name.ToString() == "Trait" && a.ArgumentList?.Arguments.Count == 2 && a.ArgumentList.Arguments[0].ToString() == "\"Size\"")
                    .Select(a => (a.ArgumentList!.Arguments[1].Expression as LiteralExpressionSyntax)?.Token.ValueText).FirstOrDefault();
                if (size is not null)
                {
                    var words = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var node in type.DescendantNodes())
                    {
                        words.Add(node switch
                        {
                            LiteralExpressionSyntax l when l.IsKind(SyntaxKind.StringLiteralExpression) => l.Token.ValueText,
                            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target } a => $"{target.Identifier.Text}.{a.Name.Identifier.Text}",
                            IdentifierNameSyntax i => i.Identifier.Text,
                            _ => string.Empty,
                        });
                    }

                    _classes.Add((new TestClass(type.Identifier.Text, size, Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/')), words));
                }
            }
        }
    }

    /// <summary>The classes that use any of the words, sorted by size, then name.</summary>
    public List<TestClass> Using(IReadOnlyCollection<string> words) =>
    [
        .. _classes.Where(c => words.Any(c.Words.Contains)).Select(c => c.Class)
            .OrderBy(c => c.Size switch { "Small" => 0, "Medium" => 1, _ => 2 }).ThenBy(c => c.Name, StringComparer.Ordinal),
    ];
}
