using System.Globalization;
using System.Reflection;
using System.Text.Json;
using EplanMcp.Coverage.Mapping;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EplanMcp.Coverage.Tests.Mapping;

[Trait("Size", "Small")]
public sealed class ToolReachReferenceTests
{
    public static TheoryData<string, string, string> Entries
    {
        get
        {
            using JsonDocument contract = JsonDocument.Parse(Resource("tool-contract.json"));
            string[] tools = [.. contract.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()!)];
            Overlay overlay = Overlay.Parse(Resource("mapping-overlay.yaml"), tools);
            Assert.NotEmpty(overlay.ReachedByTool);
            var data = new TheoryData<string, string, string>();
            foreach (ToolReach reach in overlay.ReachedByTool)
            {
                data.Add(reach.Match, reach.Source, reach.Receiver);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void Overlay_EveryReachedByToolEntry_PointsToExistingExecutableCode(string group, string call, string receiver)
    {
        Assert.StartsWith("O:", group, StringComparison.Ordinal);
        var sources = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        foreach (string reference in new[] { call, receiver })
        {
            int colon = reference.LastIndexOf(':');
            Assert.True(colon > 0, reference);
            string path = reference[..colon];
            Assert.StartsWith("src/EplanMcp.Bridge.Eplan/Adapters/", path, StringComparison.Ordinal);
            Assert.DoesNotContain("..", path, StringComparison.Ordinal);
            if (!sources.TryGetValue(path, out SyntaxTree? tree))
            {
                sources[path] = tree = CSharpSyntaxTree.ParseText(Resource(path[(path.LastIndexOf('/') + 1)..]), cancellationToken: TestContext.Current.CancellationToken);
            }
            int line = int.Parse(reference[(colon + 1)..], CultureInfo.InvariantCulture);
            var lines = tree.GetText(TestContext.Current.CancellationToken).Lines;
            Assert.InRange(line, 1, lines.Count);
            Assert.Contains(tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<ExpressionSyntax>(), e => e.Span.IntersectsWith(lines[line - 1].Span));
            Assert.Contains(reference == call ? "list[id" : ").Properties", lines[line - 1].ToString(), StringComparison.Ordinal);
        }
    }

    private static string Resource(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Overlay code reference has no embedded repository source: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
