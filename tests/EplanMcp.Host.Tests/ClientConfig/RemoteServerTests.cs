using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

[Trait("Size", "Small")]
public sealed class RemoteServerTests
{
    private const string Url = "http://192.168.1.42:8766/mcp";
    private static readonly RemoteServer _remote = new(Url);

    [Theory]
    [InlineData("http://127.0.0.1:1/mcp")]
    [InlineData("http://255.255.255.255:65535/mcp")]
    [InlineData(Url)]
    public void Url_DottedIpv4AndExplicitPort_IsAccepted(string url)
    {
        Assert.True(RemoteServer.ValidUrl(url));
        Assert.NotNull(Parse("--vm", "--url", url));
    }

    [Theory]
    [InlineData("https://192.168.1.42:8766/mcp")]
    [InlineData("http://localhost:8766/mcp")]
    [InlineData("http://vm.example:8766/mcp")]
    [InlineData("http://[::1]:8766/mcp")]
    [InlineData("http://127.1:8766/mcp")]
    [InlineData("http://127.0.0.1:0/mcp")]
    [InlineData("http://127.0.0.1:65536/mcp")]
    [InlineData("http://127.0.0.1/mcp")]
    [InlineData("http://127.0.0.1:8766/mcp?x=1")]
    public void Url_AnyOtherShape_IsRefused(string url)
    {
        Assert.False(RemoteServer.ValidUrl(url));
        Assert.Null(Parse("--vm", "--url", url));
    }

    [Theory]
    [InlineData("--vm")]
    [InlineData("--url http://127.0.0.1:8766/mcp")]
    [InlineData("--vm --remove --url http://127.0.0.1:8766/mcp")]
    [InlineData("--vm --vm --remove")]
    [InlineData("--vm --remove --dry-run")]
    [InlineData("--vm --plugin --url http://127.0.0.1:8766/mcp")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp --key secret")]
    public void Arguments_MissingOrConflictingOptions_AreRefused(string options) =>
        Assert.Null(Parse(options.Split(' ')));

    [Theory]
    [InlineData("claude-code", """{"type":"http","url":"<url>"}""")]
    [InlineData("gemini-cli", """{"httpUrl":"<url>"}""")]
    [InlineData("qwen-code", """{"httpUrl":"<url>"}""")]
    [InlineData("kimi-code", """{"url":"<url>"}""")]
    [InlineData("codebuddy", """{"type":"http","url":"<url>"}""")]
    [InlineData("opencode", """{"type":"remote","url":"<url>","enabled":true}""")]
    [InlineData("cursor", """{"url":"<url>"}""")]
    [InlineData("vscode-copilot", """{"type":"http","url":"<url>"}""")]
    [InlineData("qwen-agent", """{"type":"streamable-http","url":"<url>"}""")]
    public void Json_EachClient_HasKeyFreeRemoteEntry(string name, string expected)
    {
        McpClient client = McpClient.Find(name)!;
        Assert.True(RemoteServer.Supports(client));
        string original = """{"<servers>":{"eplan":{"command":"local","args":["serve"],"env":{"KEEP":"yes"}}},"setting":7}""".Replace("<servers>", client.ServersKey, StringComparison.Ordinal);
        ClientFileEdit edit = JsonClientFile.Write(original, client, _remote);
        JsonNode root = JsonNode.Parse(edit.Text!)!;

        Assert.Equal(expected.Replace("<url>", Url, StringComparison.Ordinal), root[client.ServersKey]![RemoteServer.Name]!.ToJsonString());
        Assert.DoesNotContain("Authorization", edit.Text!, StringComparison.Ordinal);
        Assert.Null(JsonClientFile.Write(edit.Text!, client, _remote).Text);

        ClientFileEdit removed = JsonClientFile.Remove(edit.Text!, client, RemoteServer.Name);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(removed.Text!)));
    }

    [Fact]
    public void Toml_KeyFreeRemoteTable_KeepsLocalEntries()
    {
        const string local = "[mcp_servers.eplan]\ncommand = \"local\"\nargs = [\"serve\"]\n";
        string expected = $"[mcp_servers.eplan-vm]\nurl = \"{Url}\"\nstartup_timeout_sec = 120\ntool_timeout_sec = 300\n";
        Assert.Equal(expected, CodexToml.Write(string.Empty, _remote).Text);
        ClientFileEdit edit = CodexToml.Write(local, _remote);
        Assert.Equal(local + "\n" + expected, edit.Text);
        Assert.DoesNotContain("Authorization", edit.Text!, StringComparison.Ordinal);
        Assert.Null(CodexToml.Write(edit.Text!, _remote).Text);
        Assert.Equal(local, CodexToml.Remove(edit.Text!, RemoteServer.Name).Text);
    }

    [Fact]
    public void Support_ClaudeDesktopAndUnknownClients_AreRefused()
    {
        Assert.False(RemoteServer.Supports(McpClient.Find("claude-desktop")!));
        Assert.False(RemoteServer.Supports(new("future-client", ClientFormat.McpServers)));
        Assert.Equal(10, McpClient.All.Count(RemoteServer.Supports));
    }

    private static CliArguments? Parse(params string[] options) => CliArguments.Parse(["client-config", "codex", .. options]);
}
