namespace EplanMcp.Host.Tests;

/// <summary>Both agent entry points must carry the same five hard rules, including their wording.</summary>
[Trait("Size", "Small")]
public sealed class AgentRulesTests
{
    [Fact]
    public void HardRules_AgentsAndClaude_AreIdentical()
    {
        string expected = HardRules("CLAUDE.md");
        Assert.Equal(expected, HardRules("AGENTS.md"));
        Assert.Equal(["1.", "2.", "3.", "4.", "5."], expected.Split('\n')
            .Where(line => line.Length > 1 && char.IsAsciiDigit(line[0]) && line[1] == '.')
            .Select(line => line[..2]));
    }

    private static string HardRules(string name)
    {
        using Stream stream = typeof(AgentRulesTests).Assembly.GetManifestResourceStream("AgentRules." + name)
            ?? throw new InvalidOperationException("Missing agent rules resource: " + name);
        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = text.IndexOf("## 硬性规则（违反任何一条都不能合并）\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing hard-rule heading in " + name);
        int end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        Assert.True(end > start, "Missing section boundary after hard rules in " + name);
        return text[start..end].TrimEnd('\n');
    }
}
