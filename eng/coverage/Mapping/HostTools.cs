using EplanMcp.Coverage.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Mapping;

/// <summary>One MCP tool method of the Host and the Bridge operations it invokes.</summary>
internal sealed record HostTool(string Name, string Class, string File, IReadOnlyList<string> Operations);

/// <summary>
/// Reads the Host's tool classes (src/EplanMcp.Host/Tools) as syntax: each <c>[McpServerTool(Name = …)]</c> method, the
/// <c>OperationNames</c> constants it and the same-class methods it calls use, and the names by which tests refer to the
/// tool (<c>CableTools.GetTool</c>, <c>CableTools.CableGetAsync</c>).
/// </summary>
internal static class HostTools
{
    public static (List<HostTool> Tools, Dictionary<string, string> References) Read(string repositoryRoot)
    {
        string src = Path.Combine(repositoryRoot, "src");
        Dictionary<string, string> operationNames = Constants(Parse(Path.Combine(src, "EplanMcp.Contracts", "OperationNames.cs")));
        var tools = new List<HostTool>();
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in BridgeCompilation.SourceFiles(Path.Combine(src, "EplanMcp.Host", "Tools")))
        {
            foreach (ClassDeclarationSyntax type in Parse(file).DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                Dictionary<string, string> constants = Constants(type);
                Dictionary<string, MethodDeclarationSyntax[]> methods = type.Members.OfType<MethodDeclarationSyntax>()
                    .GroupBy(m => m.Identifier.Text).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
                foreach (MethodDeclarationSyntax method in methods.Values.SelectMany(m => m))
                {
                    if (method.AttributeLists.SelectMany(l => l.Attributes).FirstOrDefault(a => a.Name.ToString() == "McpServerTool")?.ArgumentList?.Arguments
                        .FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "Name")?.Expression is not { } nameExpression)
                    {
                        continue;
                    }

                    string name = nameExpression is LiteralExpressionSyntax literal ? literal.Token.ValueText : constants[nameExpression.ToString()];
                    string[] operations = [.. Reached(method, methods)
                        .SelectMany(m => m.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                        .Where(a => a.Expression.ToString() == "OperationNames")
                        .Select(a => operationNames[a.Name.Identifier.Text]).Distinct().Order(StringComparer.Ordinal)];
                    tools.Add(new HostTool(name, type.Identifier.Text, Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'), operations));
                    references[$"{type.Identifier.Text}.{method.Identifier.Text}"] = name;
                    foreach (string constant in constants.Where(c => c.Value == name).Select(c => c.Key))
                    {
                        references[$"{type.Identifier.Text}.{constant}"] = name;
                    }
                }
            }
        }

        return (tools, references);
    }

    private static CompilationUnitSyntax Parse(string file) => (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();

    /// <summary>The const string fields of a file, or those declared directly in a type, by name.</summary>
    private static Dictionary<string, string> Constants(SyntaxNode container) =>
        container.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Modifiers.Any(SyntaxKind.ConstKeyword) && (container is CompilationUnitSyntax || f.Parent == container))
            .SelectMany(f => f.Declaration.Variables)
            .Where(v => v.Initializer?.Value is LiteralExpressionSyntax)
            .ToDictionary(v => v.Identifier.Text, v => ((LiteralExpressionSyntax)v.Initializer!.Value).Token.ValueText, StringComparer.Ordinal);

    /// <summary>The method and the same-class methods it calls by simple name, transitively.</summary>
    private static List<MethodDeclarationSyntax> Reached(MethodDeclarationSyntax start, Dictionary<string, MethodDeclarationSyntax[]> methods)
    {
        var reached = new List<MethodDeclarationSyntax>();
        var queue = new Queue<MethodDeclarationSyntax>([start]);
        while (queue.TryDequeue(out MethodDeclarationSyntax? method))
        {
            if (reached.Contains(method))
            {
                continue;
            }

            reached.Add(method);
            foreach (InvocationExpressionSyntax call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is IdentifierNameSyntax or GenericNameSyntax && methods.TryGetValue(((SimpleNameSyntax)call.Expression).Identifier.Text, out var callees))
                {
                    callees.ToList().ForEach(queue.Enqueue);
                }
            }
        }

        return reached;
    }
}
