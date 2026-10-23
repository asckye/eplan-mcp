using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Tests.ClientConfig;

[Trait("Size", "Small")]
public sealed class RemoteServerTests
{
    private const string Url = "http://192.168.1.42:8766/mcp";
    private static readonly string _key = new('A', 43);
    private static readonly RemoteServer _remote = new(Url, _key);

    [Theory]
    [InlineData("http://127.0.0.1:1/mcp")]
    [InlineData("http://255.255.255.255:65535/mcp")]
    [InlineData(Url)]
    public void Url_DottedIpv4AndExplicitPort_IsAccepted(string url)
    {
        Assert.True(RemoteServer.ValidUrl(url));
        Assert.NotNull(Parse("--vm", "--url", url, "--key-stdin"));
    }

    [Theory]
    [InlineData("https://192.168.1.42:8766/mcp")]
    [InlineData("http://localhost:8766/mcp")]
    [InlineData("http://vm.example:8766/mcp")]
    [InlineData("http://[::1]:8766/mcp")]
    [InlineData("http://127.1:8766/mcp")]
    [InlineData("http://2130706433:8766/mcp")]
    [InlineData("http://0177.0.0.1:8766/mcp")]
    [InlineData("http://256.0.0.1:8766/mcp")]
    [InlineData("http://127.0.0.1:0/mcp")]
    [InlineData("http://127.0.0.1:65536/mcp")]
    [InlineData("http://127.0.0.1:-1/mcp")]
    [InlineData("http://127.0.0.1:+1/mcp")]
    [InlineData("http://127.0.0.1/mcp")]
    [InlineData("http://127.0.0.1:8766/")]
    [InlineData("http://127.0.0.1:8766/mcp/")]
    [InlineData("http://127.0.0.1:8766/MCP")]
    [InlineData("http://127.0.0.1:8766/mcp?x=1")]
    [InlineData("http://127.0.0.1:8766/mcp#fragment")]
    [InlineData("http://user@127.0.0.1:8766/mcp")]
    [InlineData("http://127.0.0.1:8766/mcp\n")]
    [InlineData(" http://127.0.0.1:8766/mcp")]
    public void Url_AnyOtherShape_IsRefused(string url)
    {
        Assert.False(RemoteServer.ValidUrl(url));
        Assert.Null(Parse("--vm", "--url", url, "--key-stdin"));
    }

    [Theory]
    [InlineData("--vm")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp")]
    [InlineData("--url http://127.0.0.1:8766/mcp --key-stdin")]
    [InlineData("--vm --key-stdin")]
    [InlineData("--vm --remove --key-stdin")]
    [InlineData("--vm --remove --url http://127.0.0.1:8766/mcp")]
    [InlineData("--vm --vm --remove")]
    [InlineData("--vm --remove --dry-run")]
    [InlineData("--vm --plugin --url http://127.0.0.1:8766/mcp --key-stdin")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp --key-stdin --key-stdin")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp --url http://127.0.0.1:8766/mcp --key-stdin")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp --key secret")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp --key-file key.txt")]
    [InlineData("--vm --url http://127.0.0.1:8766/mcp --key-env KEY")]
    public void Arguments_MissingConflictingOrSecretOptions_AreRefused(string options) =>
        Assert.Null(Parse(options.Split(' ')));

    [Fact]
    public void Arguments_WritePreviewAndRemove_PreserveTheVmOptions()
    {
        CliArguments cli = Parse("--dry-run", "--format", "json", "--vm", "--key-stdin", "--url", Url)!;
        Assert.Equal((true, true, Url, ClientConfigMode.DryRun), (cli.Json, cli.ClientConfig!.Vm, cli.ClientConfig.Url, cli.ClientConfig.Mode));
        Assert.Equal(ClientConfigMode.Remove, Parse("--vm", "--remove")!.ClientConfig!.Mode);
        Assert.Empty(CliArguments.Parse(["client-config", "--vm", "--remove", "--format", "json"])!.ClientConfig!.Clients);
        Assert.Null(CliArguments.Parse(["client-config", "--vm", "--url", Url, "--key-stdin"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Key_ExactlyOneKeyWithAtMostOneNewline_IsAccepted(string ending)
    {
        using var input = new StringReader(_key + ending);
        RemoteServer remote = RemoteServer.Read(Url, input);
        Assert.Equal(_remote.TomlBlock("\n"), remote.TomlBlock("\n"));
        Assert.DoesNotContain(_key, remote.ToString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_GeneratedKeys_MatchTheCanonical256BitFormat()
    {
        for (int i = 0; i < 100; i++)
        {
            Assert.True(VmKey.IsValid(VmKey.Generate()));
        }

        Assert.False(VmKey.IsValid(new string('B', 43))); // Nonzero padding bits cannot be produced by VmKey.
    }

    [Fact]
    public void Key_WrongLengthsAlphabetOrLeadingWhitespace_AreRefused()
    {
        foreach (string key in new[] { _key[..42], _key + "A", " " + _key, _key.Replace('A', '+'), _key.Replace('A', '/'), new string('中', 43) })
        {
            using var input = new StringReader(key);
            Assert.Equal(ClientProblem.InvalidVmKey, Assert.Throws<ClientConfigException>(() => RemoteServer.Read(Url, input)).Problem);
        }
    }

    [Fact]
    public void Key_ExtraInput_IsReadWithAFixedBound()
    {
        using var input = new CountingReader();
        Assert.Throws<ClientConfigException>(() => RemoteServer.Read(Url, input));
        Assert.Equal(46, input.Reads);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("short")]
    [InlineData(" ", true)]
    [InlineData("\r", true)]
    [InlineData("\n\n", true)]
    [InlineData("\r\n\r\n", true)]
    [InlineData("\nextra", true)]
    [InlineData("=", true)]
    public void Key_MissingBadOrExtraInput_IsRefusedWithoutEcho(string text, bool append = false)
    {
        using var input = new StringReader(append ? _key + text : text);
        ClientConfigException error = Assert.Throws<ClientConfigException>(() => RemoteServer.Read(Url, input));
        Assert.Equal(ClientProblem.InvalidVmKey, error.Problem);
        Assert.DoesNotContain(_key, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("claude-code", """{"type":"http","url":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("gemini-cli", """{"httpUrl":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("qwen-code", """{"httpUrl":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("kimi-code", """{"url":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("codebuddy", """{"type":"http","url":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("opencode", """{"type":"remote","url":"<url>","enabled":true,"headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("cursor", """{"url":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("vscode-copilot", """{"type":"http","url":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    [InlineData("qwen-agent", """{"type":"streamable-http","url":"<url>","headers":{"Authorization":"Bearer <key>"}}""")]
    public void Json_EachClient_HasExactlyTheS19RemoteEntry(string name, string expected)
    {
        McpClient client = McpClient.Find(name)!;
        Assert.True(RemoteServer.Supports(client));
        string original = """{"<servers>":{"eplan":{"command":"local","args":["serve"],"env":{"KEEP":"yes"}}},"setting":7}""".Replace("<servers>", client.ServersKey, StringComparison.Ordinal);
        ClientFileEdit edit = JsonClientFile.Write(original, client, _remote);
        JsonNode root = JsonNode.Parse(edit.Text!)!;

        Assert.Equal(expected.Replace("<url>", Url, StringComparison.Ordinal).Replace("<key>", _key, StringComparison.Ordinal), root[client.ServersKey]![RemoteServer.Name]!.ToJsonString());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original)![client.ServersKey]!["eplan"], root[client.ServersKey]!["eplan"]));
        Assert.Equal(7, root["setting"]!.GetValue<int>());
        Assert.Null(edit.Before);
        Assert.Contains("Bearer <key>", edit.After, StringComparison.Ordinal);
        Assert.Null(JsonClientFile.Write(edit.Text!, client, _remote).Text);

        ClientFileEdit removed = JsonClientFile.Remove(edit.Text!, client, RemoteServer.Name);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(removed.Text!)));
        Assert.DoesNotContain(_key, removed.Before!, StringComparison.Ordinal);
    }

    [Fact]
    public void Toml_RemoteTableAndReplacement_KeepLocalEntriesAndRedactPreviews()
    {
        const string local = "[mcp_servers.eplan]\ncommand = \"local\"\nargs = [\"serve\"]\n";
        string expected = $"[mcp_servers.eplan-vm]\nurl = \"{Url}\"\nhttp_headers = {{ Authorization = \"Bearer {_key}\" }}\nstartup_timeout_sec = 120\ntool_timeout_sec = 300\n";
        Assert.Equal(expected, CodexToml.Write(string.Empty, _remote).Text);
        ClientFileEdit edit = CodexToml.Write(local, _remote);
        Assert.Equal(local + "\n" + expected, edit.Text);
        Assert.Contains("Bearer <key>", edit.After, StringComparison.Ordinal);
        Assert.Null(CodexToml.Write(edit.Text!, _remote).Text);
        var rotated = new RemoteServer(Url, new string('B', 42) + "A");
        ClientFileEdit update = CodexToml.Write(edit.Text!, rotated);
        Assert.StartsWith(local, update.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(_key, update.Before!, StringComparison.Ordinal);
        Assert.Equal(local, CodexToml.Remove(update.Text!, RemoteServer.Name).Text);
        Assert.Equal(expected.ReplaceLineEndings("\r\n"), CodexToml.Write("\r\n", _remote).Text);
    }

    [Fact]
    public void Json_Update_ReplacesTheRemoteEntryAndHidesBothKeys()
    {
        McpClient client = McpClient.Find("cursor")!;
        string original = """{"mcpServers":{"eplan-vm":{"command":"old","env":{"OLD":"option"},"headers":{"Authorization":"Bearer <key>"}},"eplan":{"command":"local"}}}""".Replace("<key>", _key, StringComparison.Ordinal);
        string replacement = new string('B', 42) + "A";
        ClientFileEdit edit = JsonClientFile.Write(original, client, new RemoteServer(Url, replacement));
        JsonNode entry = JsonNode.Parse(edit.Text!)!["mcpServers"]!["eplan-vm"]!;

        Assert.Equal(["url", "headers"], entry.AsObject().Select(p => p.Key));
        Assert.Equal("Bearer " + replacement, entry["headers"]!["Authorization"]!.GetValue<string>());
        Assert.DoesNotContain(_key, edit.Before!, StringComparison.Ordinal);
        Assert.DoesNotContain(replacement, edit.After!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mcp_servers = { eplan-vm = { url = 'old' } }\n")]
    [InlineData("[mcp_servers]\neplan-vm = {}\n")]
    [InlineData("[[mcp_servers.eplan-vm]]\nurl = 'old'\n")]
    [InlineData("[mcp_servers.eplan-vm]\nurl = 'unclosed\n")]
    public void Toml_UnsafeForms_AreRefused(string original) =>
        Assert.Throws<ClientConfigException>(() => CodexToml.Write(original, _remote));

    [Fact]
    public void Support_ClaudeDesktopAndUnknownClients_AreRefused()
    {
        Assert.False(RemoteServer.Supports(McpClient.Find("claude-desktop")!));
        Assert.False(RemoteServer.Supports(new("future-client", ClientFormat.McpServers)));
        Assert.Equal(10, McpClient.All.Count(RemoteServer.Supports));
    }

    [Fact]
    public void Output_AllModesLanguagesAndProblemDetails_RedactAnyKey()
    {
        foreach (ClientConfigMode mode in Enum.GetValues<ClientConfigMode>())
        {
            var result = new ClientResult("codex", _key, ClientAction.Write, mode == ClientConfigMode.DryRun ? ClientOutcome.WouldWrite : ClientOutcome.Written)
            {
                Before = "Bearer " + _key,
                After = "Bearer " + _key,
                Backup = _key,
                Problem = new(ClientProblem.UnsupportedToml, _key),
            };
            var report = new ClientConfigReport(new(_key), mode, null, [result], Vm: true, Url: Url);
            string json = ClientConfigOutput.Json(report);
            Assert.DoesNotContain(_key, json, StringComparison.Ordinal);
            Assert.Equal(2, JsonNode.Parse(json)!["schemaVersion"]!.GetValue<int>());
            Assert.Equal("eplan-vm", JsonNode.Parse(json)!["server"]!["name"]!.GetValue<string>());
            foreach (string language in new[] { "en-US", "zh-CN" })
            {
                Assert.DoesNotContain(_key, ClientConfigOutput.Text(report, CultureInfo.GetCultureInfo(language)), StringComparison.Ordinal);
            }

            Assert.DoesNotContain(_key, ClientConfigOutput.Text(report with { Problem = new(ClientProblem.InvalidRecord, _key) }, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }

    private static CliArguments? Parse(params string[] options) => CliArguments.Parse(["client-config", "codex", .. options]);

    private sealed class CountingReader : TextReader
    {
        public int Reads { get; private set; }

        public override int Read()
        {
            Reads++;
            return 'A';
        }
    }
}
