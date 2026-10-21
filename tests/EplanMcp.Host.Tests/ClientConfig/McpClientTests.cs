using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>The client table of <c>client-config</c> (M5 design 4.11): names, files and entry formats, as TIA writes them.</summary>
[Trait("Size", "Small")]
public sealed class McpClientTests
{
    private const string Home = @"C:\Users\工程师";

    private static readonly UserFolders _folders = Folders(_ => null);

    [Fact]
    public void All_AreTheElevenClientsOfDesignQ7InOrder()
    {
        Assert.Equal(
            ["claude-code", "claude-desktop", "codex", "gemini-cli", "qwen-code", "kimi-code", "codebuddy", "opencode", "qwen-agent", "cursor", "vscode-copilot"],
            McpClient.All.Select(c => c.Name));
    }

    [Theory]
    [InlineData("claude-code", @"C:\Users\工程师\.claude.json")]
    [InlineData("codex", @"C:\Users\工程师\.codex\config.toml")]
    [InlineData("gemini-cli", @"C:\Users\工程师\.gemini\settings.json")]
    [InlineData("qwen-code", @"C:\Users\工程师\.qwen\settings.json")]
    [InlineData("kimi-code", @"C:\Users\工程师\.kimi-code\mcp.json")]
    [InlineData("codebuddy", @"C:\Users\工程师\.codebuddy\.mcp.json")]
    [InlineData("opencode", @"C:\Users\工程师\.config\opencode\opencode.json")]
    [InlineData("qwen-agent", @"C:\Users\工程师\.qwen-agent\mcp.json")]
    [InlineData("cursor", @"C:\Users\工程师\.cursor\mcp.json")]
    public void ConfigFile_IsTheFileOfTheDesignTable(string name, string file)
    {
        Assert.Equal(file, Client(name).ConfigFile(_folders));
    }

    [Theory]
    [InlineData("codex", "CODEX_HOME", @"D:\codex", @"D:\codex\config.toml")]
    [InlineData("kimi-code", "KIMI_CODE_HOME", @"D:\kimi", @"D:\kimi\mcp.json")]
    public void ConfigFile_FollowsTheClientsOwnHomeVariable(string name, string variable, string value, string file)
    {
        UserFolders folders = Folders(v => v == variable ? value : null);

        Assert.Equal(file, Client(name).ConfigFile(folders));
    }

    [Theory]
    [InlineData("codex", "")]
    [InlineData("codex", "   ")]
    [InlineData("kimi-code", "")]
    public void ConfigFile_IgnoresABlankHomeVariable(string name, string value)
    {
        UserFolders folders = Folders(_ => value);

        Assert.Equal(Client(name).ConfigFile(_folders), Client(name).ConfigFile(folders));
    }

    [Fact]
    public void ConfigFile_ReadsNoOtherVariable()
    {
        var asked = new List<string>();
        UserFolders folders = Folders(v =>
        {
            asked.Add(v);
            return @"D:\elsewhere";
        });

        foreach (McpClient client in McpClient.All.Where(c => c.Name is not ("claude-desktop" or "vscode-copilot")))
        {
            client.ConfigFile(folders);
        }

        Assert.Equal(["CODEX_HOME", "KIMI_CODE_HOME"], asked);
    }

    [Theory]
    [InlineData("claude-code", "mcpServers")]
    [InlineData("claude-desktop", "mcpServers")]
    [InlineData("codex", "mcp_servers")]
    [InlineData("gemini-cli", "mcpServers")]
    [InlineData("qwen-code", "mcpServers")]
    [InlineData("kimi-code", "mcpServers")]
    [InlineData("codebuddy", "mcpServers")]
    [InlineData("opencode", "mcp")]
    [InlineData("qwen-agent", "mcpServers")]
    [InlineData("cursor", "mcpServers")]
    [InlineData("vscode-copilot", "servers")]
    public void ServersKey_IsTheClientsKey(string name, string key)
    {
        Assert.Equal(key, Client(name).ServersKey);
    }

    [Theory]
    [InlineData("claude-code", """{"type":"stdio","command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("claude-desktop", """{"command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("gemini-cli", """{"command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("qwen-code", """{"command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("kimi-code", """{"command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("codebuddy", """{"type":"stdio","command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("opencode", """{"type":"local","command":["C:\\EplanMcp\\app\\eplanmcp.exe","serve"],"enabled":true}""")]
    [InlineData("qwen-agent", """{"command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("cursor", """{"type":"stdio","command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    [InlineData("vscode-copilot", """{"type":"stdio","command":"C:\\EplanMcp\\app\\eplanmcp.exe","args":["serve"]}""")]
    public void JsonEntry_IsTheLocalEntryTiaWrites(string name, string entry)
    {
        JsonObject json = Client(name).JsonEntry(new LocalServer(@"C:\EplanMcp\app\eplanmcp.exe"));

        Assert.Equal(entry, json.ToJsonString());
    }

    [Fact]
    public void JsonEntry_OfCodex_IsRefused()
    {
        Assert.Throws<InvalidOperationException>(() => Client("codex").JsonEntry(new LocalServer(@"C:\eplanmcp.exe")));
    }

    [Fact]
    public void LocalServer_IsEplanServe()
    {
        Assert.Equal("eplan", LocalServer.Name);
        Assert.Equal(["serve"], LocalServer.Arguments);
    }

    [Fact]
    public void Find_KnowsOnlyTheTableNames()
    {
        Assert.Same(McpClient.All[2], McpClient.Find("codex"));
        Assert.Null(McpClient.Find("Codex"));
        Assert.Null(McpClient.Find("vscode"));
    }

    private static McpClient Client(string name) => McpClient.Find(name)!;

    private static UserFolders Folders(Func<string, string?> variable) =>
        new(Home, Home + @"\AppData\Roaming", Home + @"\AppData\Local", variable);
}
