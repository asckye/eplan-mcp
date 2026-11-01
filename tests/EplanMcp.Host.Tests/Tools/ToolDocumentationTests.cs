namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ToolDocumentationTests
{
    [Fact]
    public void RegisteredTools_HaveIndexRowsAndImplementedSpecifications()
    {
        string[] rows = Read("README.md").Split('\n');

        foreach (string tool in ReviewedToolContract.Names)
        {
            string row = Assert.Single(rows, row => row.StartsWith($"| `{tool}` |", StringComparison.Ordinal));
            Assert.Contains($"| [{tool}.md]({tool}.md) |", row, StringComparison.Ordinal);

            string specification = Read(tool + ".md");
            Assert.Matches(@"(?m)^\| 状态 \| Implemented \((?:v\d+\.\d+\.\d+|Unreleased)\) \|\r?$", specification);
            Assert.DoesNotMatch(@"(?m)^\|[^|\r\n]*状态[^|\r\n]*\|[^\r\n]*\bProposed\b", specification);
        }
    }

    private static string Read(string name)
    {
        using Stream stream = typeof(ToolDocumentationTests).Assembly.GetManifestResourceStream("ToolDocs." + name)
            ?? throw new InvalidOperationException("Missing tool documentation resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
