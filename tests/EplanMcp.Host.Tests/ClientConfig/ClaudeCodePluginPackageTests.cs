using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>Release sources embedded at build time: no client settings, installed programs, or EPLAN are touched.</summary>
[Trait("Size", "Small")]
public sealed partial class ClaudeCodePluginPackageTests
{
    private static readonly string[] _serverFields = ["args", "command"];

    [Fact]
    public void Manifests_MatchClientConfigIdentityAndProductVersion()
    {
        using JsonDocument marketplace = JsonDocument.Parse(Read("marketplace.json"));
        using JsonDocument plugin = JsonDocument.Parse(Read("plugin.json"));
        JsonElement entry = Assert.Single(marketplace.RootElement.GetProperty("plugins").EnumerateArray());
        string? name = plugin.RootElement.GetProperty("name").GetString();

        Assert.Equal(ClaudeCodePlugin.Id, name + "@" + marketplace.RootElement.GetProperty("name").GetString());
        Assert.Equal(name, entry.GetProperty("name").GetString());
        Assert.Equal("./" + name, entry.GetProperty("source").GetString());
        Assert.Equal(HostInfo.Version, plugin.RootElement.GetProperty("version").GetString());
        Assert.Equal("MIT", plugin.RootElement.GetProperty("license").GetString());
        Assert.False(string.IsNullOrWhiteSpace(plugin.RootElement.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(marketplace.RootElement.GetProperty("owner").GetProperty("name").GetString()));
    }

    [Fact]
    public void McpServer_UsesFixedAppPathAndSameArgumentsAsClientConfig()
    {
        using JsonDocument mcp = JsonDocument.Parse(Read("mcp.json"));
        JsonProperty server = Assert.Single(mcp.RootElement.GetProperty("mcpServers").EnumerateObject());

        Assert.Equal(LocalServer.Name, server.Name);
        Assert.Equal("${USERPROFILE}/EplanMcp/app/eplanmcp.exe", server.Value.GetProperty("command").GetString());
        Assert.Equal(LocalServer.Arguments, server.Value.GetProperty("args").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(_serverFields, server.Value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Skill_OnlyNamesToolsFromTheCatalog()
    {
        using JsonDocument catalog = JsonDocument.Parse(Read("tools.json"));
        HashSet<string> known = catalog.RootElement.GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
        string skill = Read("SKILL.md");
        string[] mentioned = [.. ToolName().Matches(skill).Select(m => m.Value).Distinct(StringComparer.Ordinal)];

        Assert.NotEmpty(mentioned);
        Assert.Contains("eplan_session_status", mentioned);
        Assert.Contains("eplan_tool_usage", mentioned);
        Assert.All(mentioned, tool => Assert.Contains(tool, known));
        Assert.DoesNotMatch(@"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}]", skill);
    }

    [Fact]
    public void SkillGuide_MatchesTheCanonicalChineseGuide()
    {
        Assert.Contains("(guide.zh-CN.md)", Read("SKILL.md"), StringComparison.Ordinal);
        Assert.Equal(Read("source-guide.zh-CN.md"), Read("guide.zh-CN.md"));
    }

    private static string Read(string name)
    {
        using Stream stream = typeof(ClaudeCodePluginPackageTests).Assembly.GetManifestResourceStream("ClaudePlugin." + name)
            ?? throw new InvalidOperationException("Missing plugin test resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\beplan_[a-z][a-z0-9_]*\b", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();
}
