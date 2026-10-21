using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>The merge into the JSON clients' files: only the eplan entry changes, everything else stays.</summary>
[Trait("Size", "Small")]
public sealed class JsonClientFileTests
{
    private const string Exe = @"C:\Users\工程师\EplanMcp\app\eplanmcp.exe";

    private static readonly LocalServer _server = new(Exe);

    public static TheoryData<string> JsonClients => [.. McpClient.All.Where(c => c.Format != ClientFormat.CodexToml).Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(JsonClients))]
    public void Write_ToANewFile_AddsTheServerMapWithTheEntry(string name)
    {
        McpClient client = McpClient.Find(name)!;

        ClientFileEdit edit = JsonClientFile.Write(string.Empty, client, _server);

        JsonObject root = Parse(edit.Text);
        Assert.Equal([client.ServersKey], root.Select(p => p.Key));
        Assert.True(JsonNode.DeepEquals(client.JsonEntry(_server), root[client.ServersKey]![LocalServer.Name]));
        Assert.Null(edit.Before);
        Assert.StartsWith("\"eplan\": {", edit.After, StringComparison.Ordinal);
        Assert.False(edit.CommentsDropped);
    }

    [Theory]
    [MemberData(nameof(JsonClients))]
    public void Write_KeepsOtherServersAndSettingsInTheirOrder(string name)
    {
        McpClient client = McpClient.Find(name)!;
        string original = $$"""
            {
              "theme": "dark",
              "{{client.ServersKey}}": {
                "github": { "command": "npx", "env": { "GITHUB_TOKEN": "ghp_secret" } },
                "remote": { "url": "https://example.test/mcp", "headers": { "Authorization": "Bearer secret" } }
              },
              "projects": { "D:\\work": { "mcpServers": { "eplan": { "command": "old.exe" } } } },
              "count": 1.50
            }
            """;

        ClientFileEdit edit = JsonClientFile.Write(original, client, _server);

        JsonObject before = Parse(original);
        JsonObject after = Parse(edit.Text);
        Assert.Equal(["theme", client.ServersKey, "projects", "count"], after.Select(p => p.Key));
        Assert.Equal(["github", "remote", "eplan"], after[client.ServersKey]!.AsObject().Select(p => p.Key));
        Assert.True(JsonNode.DeepEquals(before[client.ServersKey]!["github"], after[client.ServersKey]!["github"]));
        Assert.True(JsonNode.DeepEquals(before[client.ServersKey]!["remote"], after[client.ServersKey]!["remote"]));
        Assert.True(JsonNode.DeepEquals(before["projects"], after["projects"]));
        Assert.Contains("\"count\": 1.50", edit.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", edit.After, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OverAnOlderEntry_ChangesOnlyCommandAndArgs()
    {
        string original = """
            {"mcpServers": {
              "a": {"command": "a.exe"},
              "eplan": {"type": "stdio", "command": "D:\\dev\\eplanmcp.exe", "args": ["serve", "--old"], "env": {"EPLANMCP_HOME": "D:\\data"}, "timeout": 60},
              "z": {"command": "z.exe"}
            }}
            """;

        ClientFileEdit edit = JsonClientFile.Write(original, Client("claude-code"), _server);

        JsonObject servers = Parse(edit.Text)["mcpServers"]!.AsObject();
        Assert.Equal(["a", "eplan", "z"], servers.Select(p => p.Key));
        Assert.Equal(
            """{"type":"stdio","command":"C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe","args":["serve"],"env":{"EPLANMCP_HOME":"D:\\data"},"timeout":60}""",
            servers["eplan"]!.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.Contains(@"D:\\dev\\eplanmcp.exe", edit.Before, StringComparison.Ordinal);
        Assert.Contains("EPLANMCP_HOME", edit.After, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_OverAnOlderOpenCodeEntry_ReplacesTheCommandArrayAndKeepsEnabled()
    {
        string original = """{"mcp": {"eplan": {"type": "local", "command": ["D:\\dev\\eplanmcp.exe", "serve"], "enabled": false}}}""";

        ClientFileEdit edit = JsonClientFile.Write(original, Client("opencode"), _server);

        Assert.Equal(
            """{"type":"local","command":["C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe","serve"],"enabled":false}""",
            Parse(edit.Text)["mcp"]!["eplan"]!.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    [Fact]
    public void Write_OverAnEntryWithoutACommand_ReplacesIt()
    {
        string original = """{"mcpServers": {"eplan": {"url": "http://10.0.0.5:8766/mcp"}}}""";

        ClientFileEdit edit = JsonClientFile.Write(original, Client("cursor"), _server);

        Assert.True(JsonNode.DeepEquals(Client("cursor").JsonEntry(_server), Parse(edit.Text)["mcpServers"]!["eplan"]));
        Assert.Contains("10.0.0.5", edit.Before, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(JsonClients))]
    public void Write_WhenTheEntryIsAlreadyCurrent_LeavesTheFileAlone(string name)
    {
        McpClient client = McpClient.Find(name)!;
        string current = JsonClientFile.Write("{\"other\": 1}", client, _server).Text!;

        ClientFileEdit edit = JsonClientFile.Write(current, client, _server);

        Assert.Null(edit.Text);
        Assert.NotNull(edit.Before);
        Assert.Equal(edit.Before, edit.After);
    }

    [Fact]
    public void Write_Jsonc_ReadsCommentsAndTrailingCommasAndSaysTheCommentsAreDropped()
    {
        string original = """
            // VS Code MCP servers
            {
              "servers": {
                /* the team's server */
                "team": { "type": "http", "url": "https://example.test/mcp", },
              },
              "inputs": [],
            }
            """;

        ClientFileEdit edit = JsonClientFile.Write(original, Client("vscode-copilot"), _server);

        JsonObject after = Parse(edit.Text);
        Assert.Equal(["team", "eplan"], after["servers"]!.AsObject().Select(p => p.Key));
        Assert.Equal("https://example.test/mcp", after["servers"]!["team"]!["url"]!.GetValue<string>());
        Assert.DoesNotContain("//", edit.Text!.Replace("https://", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(edit.CommentsDropped);
    }

    [Fact]
    public void Write_TrailingCommasAlone_DropNoComments()
    {
        ClientFileEdit edit = JsonClientFile.Write("""{"mcpServers": {"a": {"command": "a.exe",},},}""", Client("gemini-cli"), _server);

        Assert.False(edit.CommentsDropped);
        Assert.Equal(["a", "eplan"], Parse(edit.Text)["mcpServers"]!.AsObject().Select(p => p.Key));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("// only a comment")]
    [InlineData("{/* not closed }")]
    [InlineData("""{"mcpServers": []}""")]
    [InlineData("""{"mcpServers": null}""")]
    [InlineData("""{"mcpServers": {"eplan": "eplanmcp.exe"}}""")]
    [InlineData("""{"mcpServers": {"eplan": null}}""")]
    [InlineData("""{"a": 1, "a": 2}""")]
    [InlineData("""{"mcpServers": {"x": {}, "x": {}}}""")]
    public void Write_ToAnInvalidFile_IsRefused(string original)
    {
        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => JsonClientFile.Write(original, Client("claude-code"), _server));

        Assert.Equal(ClientProblem.InvalidFile, refused.Problem);
        Assert.NotEmpty(refused.Detail);
    }

    [Theory]
    [InlineData("{\r\n  \"x\": 1\r\n}\r\n", "\r\n")]
    [InlineData("{\n  \"x\": 1\n}\n", "\n")]
    [InlineData("{\"x\": 1}", "\n")]
    public void Write_KeepsTheFilesLineEnds(string original, string newline)
    {
        string text = JsonClientFile.Write(original, Client("claude-code"), _server).Text!;

        Assert.EndsWith("}" + newline, text, StringComparison.Ordinal);
        Assert.Equal(text.Split('\n').Length - 1, CountOf(text, newline));
    }

    [Fact]
    public void Write_KeepsTextReadable()
    {
        string original = """{"mcpServers": {"中文": {"command": "D:\\工具\\a.exe", "args": ["<x>&'y'"]}}}""";

        string text = JsonClientFile.Write(original, Client("kimi-code"), _server).Text!;

        Assert.Contains("\"中文\"", text, StringComparison.Ordinal);
        Assert.Contains("<x>&'y'", text, StringComparison.Ordinal);
        Assert.Contains(@"C:\\Users\\工程师\\EplanMcp\\app\\eplanmcp.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_TakesOutOnlyTheEntry()
    {
        string original = """{"mcpServers": {"a": {"command": "a.exe"}, "eplan": {"command": "C:\\eplanmcp.exe", "args": ["serve"]}, "b": {}}, "x": true}""";

        ClientFileEdit edit = JsonClientFile.Remove(original, Client("qwen-agent"), LocalServer.Name);

        JsonObject after = Parse(edit.Text);
        Assert.Equal(["a", "b"], after["mcpServers"]!.AsObject().Select(p => p.Key));
        Assert.True(after["x"]!.GetValue<bool>());
        Assert.StartsWith("\"eplan\": {", edit.Before, StringComparison.Ordinal);
        Assert.Null(edit.After);
    }

    [Fact]
    public void Remove_ANamedEntry_LeavesTheOthers()
    {
        string original = """{"servers": {"eplan": {"command": "C:\\eplanmcp.exe"}, "eplan-vm": {"url": "http://10.0.0.5:8766/mcp"}}}""";

        ClientFileEdit edit = JsonClientFile.Remove(original, Client("vscode-copilot"), "eplan-vm");

        Assert.Equal(["eplan"], Parse(edit.Text)["servers"]!.AsObject().Select(p => p.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    [InlineData("{}")]
    [InlineData("""{"mcpServers": {}}""")]
    [InlineData("""{"mcpServers": {"other": {}}}""")]
    [InlineData("""{"servers": {"eplan": {}}}""")]
    public void Remove_WithoutTheEntry_LeavesTheFileAlone(string original)
    {
        Assert.Equal(new ClientFileEdit(null, null, null), JsonClientFile.Remove(original, Client("codebuddy"), LocalServer.Name));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"mcpServers": 1}""")]
    [InlineData("""{"mcpServers": {"eplan": {}, "eplan": {}}}""")]
    public void Remove_FromAnInvalidFile_IsRefused(string original)
    {
        ClientConfigException refused = Assert.Throws<ClientConfigException>(() => JsonClientFile.Remove(original, Client("claude-desktop"), LocalServer.Name));

        Assert.Equal(ClientProblem.InvalidFile, refused.Problem);
    }

    [Fact]
    public void Remove_AfterWrite_GivesBackTheServers()
    {
        string original = """{"mcpServers": {"a": {"command": "a.exe"}}}""";
        string written = JsonClientFile.Write(original, Client("claude-code"), _server).Text!;

        ClientFileEdit edit = JsonClientFile.Remove(written, Client("claude-code"), LocalServer.Name);

        Assert.True(JsonNode.DeepEquals(Parse(original), Parse(edit.Text)));
    }

    private static McpClient Client(string name) => McpClient.Find(name)!;

    private static JsonObject Parse(string? text) => JsonNode.Parse(text!)!.AsObject();

    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;
}
