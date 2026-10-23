using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Tests.Safety;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>Only temporary program, data and user folders; no Claude process or user settings are used.</summary>
[Trait("Size", "Medium")]
public sealed class ClaudeCodePluginGenerationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-plugin-generation-").FullName;
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));

    private string Program => Path.Combine(_root, "Engineer's $tools", "An arbitrary program folder");
    private string Data => Path.Combine(_root, "Separate data");
    private string Profile => Path.Combine(_root, "Fake user");
    private string McpFile => Path.Combine(Data, "claude-plugin", "eplan-mcp", ".mcp.json");
    private UserFolders Folders => new(Profile, Path.Combine(Profile, "Roaming"), Path.Combine(Profile, "Local"), _ => null);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Preview_WritesNothing_ThenGenerationCopiesDotDirectoriesAndWritesTheActualHostPath()
    {
        Template(Program);
        ClientConfigReport preview = Run(Program, dryRun: true);

        Assert.False(preview.Failed);
        Assert.Equal(ClientOutcome.WouldWrite, Assert.Single(preview.Clients).Outcome);
        Assert.Contains("An arbitrary program folder", preview.Clients[0].After, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Data));

        ClientConfigReport report = Run(Program);

        Assert.False(report.Failed, report.Clients[0].Problem?.Detail);
        Assert.Equal(McpFile, Assert.Single(report.Clients).File);
        Assert.Equal(Path.Combine(Program, "eplanmcp.exe"), JsonNode.Parse(File.ReadAllText(McpFile))!["mcpServers"]!["eplan"]!["command"]!.GetValue<string>());
        Assert.Equal("serve", JsonNode.Parse(File.ReadAllText(McpFile))!["mcpServers"]!["eplan"]!["args"]![0]!.GetValue<string>());
        Assert.Equal(Read("marketplace.json"), File.ReadAllText(Path.Combine(Data, "claude-plugin", ".claude-plugin", "marketplace.json")));
        Assert.Equal(Read("SKILL.md"), File.ReadAllText(Path.Combine(Data, "claude-plugin", "eplan-mcp", "skills", "getting-started", "SKILL.md")));
        Assert.Contains("__EPLANMCP_EXE__", File.ReadAllText(Path.Combine(Program, "claude-plugin", "eplan-mcp", ".mcp.json")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Profile));
        Assert.False(Directory.Exists(Path.Combine(Data, "state")));
    }

    [Fact]
    public void MovingThePrograms_RegeneratesTheAbsolutePath_AndBacksUpThePreviousMcpFile()
    {
        Template(Program);
        Assert.False(Run(Program).Failed);
        byte[] before = File.ReadAllBytes(McpFile);
        string moved = Path.Combine(_root, "New permanent folder");
        Template(moved);
        _time.Advance(TimeSpan.FromMinutes(1));

        ClientConfigReport report = Run(moved);

        Assert.False(report.Failed, report.Clients[0].Problem?.Detail);
        Assert.Equal(before, File.ReadAllBytes(report.Clients[0].Backup!));
        Assert.Equal(Path.Combine(moved, "eplanmcp.exe"), JsonNode.Parse(File.ReadAllText(McpFile))!["mcpServers"]!["eplan"]!["command"]!.GetValue<string>());
        Assert.False(Run(moved).Failed);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(McpFile)!, "*.bak_*"));
    }

    [Fact]
    public void ExistingExplicitConfiguration_RefusesThePlugin_AndInstalledPluginRefusesExplicitConfiguration()
    {
        Template(Program);
        Directory.CreateDirectory(Profile);
        string config = Path.Combine(Profile, ".claude.json");
        File.WriteAllText(config, """{"mcpServers":{"eplan":{"command":"old"}}}""");

        ClientConfigReport report = Run(Program);

        Assert.Equal(ClientProblem.ClientConfigured, report.Clients[0].Problem!.Problem);
        Assert.False(Directory.Exists(Data));
        Assert.Contains("old", File.ReadAllText(config), StringComparison.Ordinal);
        File.WriteAllText(config, "{}");
        Directory.CreateDirectory(Path.Combine(Profile, ".claude"));
        File.WriteAllText(Path.Combine(Profile, ".claude", "settings.json"), """{"enabledPlugins":{"eplan-mcp@eplanmcp-local":true}}""");
        Assert.False(Run(Program).Failed); // regeneration remains available with the plugin installed
        ClientConfigReport explicitConfig = Command(Program).Run(new([McpClient.Find("claude-code")!], ClientConfigMode.Write));
        Assert.Equal(ClientProblem.PluginInstalled, explicitConfig.Clients[0].Problem!.Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("claude-plugin")]
    [InlineData("claude-plugin/child")]
    public void OverlappingTemplateAndDataDirectories_AreRefusedBeforeChangingTheTemplate(string relative)
    {
        Template(Program);
        var command = new ClientConfigCommand(Folders, new HostDataLocations(Path.Combine(Program, relative)), Path.Combine(Program, "eplanmcp.exe"), _time, () => false, () => null);

        Assert.True(command.Run(new([McpClient.Find("claude-code")!], ClientConfigMode.Write, Plugin: true)).Failed);
        Assert.Contains("__EPLANMCP_EXE__", File.ReadAllText(Path.Combine(Program, "claude-plugin", "eplan-mcp", ".mcp.json")), StringComparison.Ordinal);
    }

    private ClientConfigReport Run(string program, bool dryRun = false) => Command(program).Run(new([McpClient.Find("claude-code")!], dryRun ? ClientConfigMode.DryRun : ClientConfigMode.Write, Plugin: true));

    private ClientConfigCommand Command(string program) => new(Folders, new HostDataLocations(Data), Path.Combine(program, "eplanmcp.exe"), _time, () => false, () => null);

    private static void Template(string program)
    {
        foreach ((string path, string resource) in new[]
        {
            (".claude-plugin/marketplace.json", "marketplace.json"),
            ("eplan-mcp/.claude-plugin/plugin.json", "plugin.json"),
            ("eplan-mcp/.mcp.json", "mcp.json"),
            ("eplan-mcp/skills/getting-started/SKILL.md", "SKILL.md"),
        })
        {
            string file = Path.Combine(program, "claude-plugin", path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, Read(resource));
        }
    }

    private static string Read(string resource)
    {
        using Stream stream = typeof(ClaudeCodePluginGenerationTests).Assembly.GetManifestResourceStream("ClaudePlugin." + resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
