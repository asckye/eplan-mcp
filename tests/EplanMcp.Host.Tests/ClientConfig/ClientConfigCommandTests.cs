using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Tests.Safety;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>
/// client-config end to end in this process, over a temporary profile and data root: the files, the backups and the
/// record. Never the user's own client files: the profile comes in through UserFolders' test constructor. Medium.
/// </summary>
[Trait("Size", "Medium")]
public sealed class ClientConfigCommandTests : IDisposable
{
    private const string Backup = ".bak_20261021T0815300000000Z";

    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-client-config-").FullName;
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 8, 15, 30, TimeSpan.Zero));

    private string Profile => Path.Combine(_root, "用户");

    private string Exe => Path.Combine(_root, "app", "eplanmcp.exe");

    private UserFolders Folders => new(Profile, Path.Combine(Profile, "AppData", "Roaming"), Path.Combine(Profile, "AppData", "Local"), _ => null);

    private ClientRecord Record => ClientRecord.For(new HostDataLocations(Path.Combine(_root, "数据")));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Write_EveryClientIntoAnEmptyProfile_CreatesEachFileAndRecordsIt()
    {
        ClientConfigReport report = Run(ClientConfigMode.Write, [.. McpClient.All.Select(c => c.Name)]);

        Assert.False(report.Failed);
        Assert.All(report.Clients, r => Assert.Equal((ClientOutcome.Written, (string?)null), (r.Outcome, r.Backup)));
        foreach (McpClient client in McpClient.All)
        {
            string file = client.ConfigFile(Folders);
            Assert.Equal(file, report.Clients.Single(r => r.Client == client.Name).File);
            Assert.True(client.Format == ClientFormat.CodexToml
                ? File.ReadAllText(file).StartsWith("[mcp_servers.eplan]\ncommand = ", StringComparison.Ordinal)
                : JsonNode.DeepEquals(client.JsonEntry(new(Exe)), JsonNode.Parse(File.ReadAllText(file))![client.ServersKey]![LocalServer.Name]));
        }

        Assert.Equal(McpClient.All.Select(c => (c.Name, c.ConfigFile(Folders), "eplan")), Record.All().Select(e => (e.Client, e.File, e.Entry)));
        Assert.Equal(McpClient.All.Count + 1, Files().Count);
    }

    [Fact]
    public void Write_Again_LeavesTheFilesAloneAndMakesNoBackup()
    {
        Run(ClientConfigMode.Write, "codex", "cursor");
        Dictionary<string, byte[]> before = Contents();
        _time.Advance(TimeSpan.FromHours(1));

        ClientConfigReport report = Run(ClientConfigMode.Write, "codex", "cursor");

        Assert.All(report.Clients, r => Assert.Equal((ClientOutcome.Unchanged, (string?)null), (r.Outcome, r.Backup)));
        Assert.Equal(before.Keys, Contents().Keys);
        Assert.All(before.Where(f => !f.Key.EndsWith("clients.json", StringComparison.Ordinal)), f => Assert.Equal(f.Value, File.ReadAllBytes(f.Key)));
        Assert.All(Record.All(), e => Assert.Equal(_time.Now, e.Time));
    }

    [Fact]
    public void Write_OverExistingFiles_BacksThemUpAndKeepsEverythingElse()
    {
        string claude = Put("claude-code", """{"numStartups": 42, "mcpServers": {"github": {"command": "npx", "env": {"TOKEN": "ghp_secret"}}}}""");
        string codex = Put("codex", "model = \"o3\"\n\n[mcp_servers.github]\ncommand = \"npx\"\n");
        string vscode = Put("vscode-copilot", "// mine\n{\"servers\": {\"team\": {\"type\": \"http\", \"url\": \"https://example.test/mcp\"}}}\n");
        Dictionary<string, byte[]> originals = Contents();

        ClientConfigReport report = Run(ClientConfigMode.Write, "claude-code", "codex", "vscode-copilot");

        Assert.All(report.Clients, r => Assert.Equal((ClientOutcome.Written, r.File + Backup), (r.Outcome, r.Backup)));
        Assert.All(new[] { claude, codex, vscode }, f => Assert.Equal(originals[f], File.ReadAllBytes(f + Backup)));
        Assert.Equal(["github", "eplan"], JsonNode.Parse(File.ReadAllText(claude))!["mcpServers"]!.AsObject().Select(p => p.Key));
        Assert.Equal(42, JsonNode.Parse(File.ReadAllText(claude))!["numStartups"]!.GetValue<int>());
        Assert.StartsWith("model = \"o3\"\n\n[mcp_servers.github]\ncommand = \"npx\"\n\n[mcp_servers.eplan]\n", File.ReadAllText(codex), StringComparison.Ordinal);
        Assert.Equal([false, false, true], report.Clients.Select(r => r.CommentsDropped));
        Assert.DoesNotContain(report.Clients, r => (r.Before + r.After).Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void DryRun_ShowsTheChangeAndWritesNothing()
    {
        Put("cursor", """{"mcpServers": {"eplan": {"command": "D:\\old\\eplanmcp.exe", "args": ["serve"]}}}""");
        Dictionary<string, byte[]> before = Contents();

        ClientConfigReport report = Run(ClientConfigMode.DryRun, [.. McpClient.All.Select(c => c.Name)]);

        Assert.All(report.Clients, r => Assert.Equal((ClientOutcome.WouldWrite, (string?)null), (r.Outcome, r.Backup)));
        ClientResult cursor = report.Clients.Single(r => r.Client == "cursor");
        Assert.Contains(@"D:\\old\\eplanmcp.exe", cursor.Before, StringComparison.Ordinal);
        Assert.Contains(Exe.Replace(@"\", @"\\", StringComparison.Ordinal), cursor.After, StringComparison.Ordinal);
        Assert.Equal(before.Keys, Contents().Keys);
        Assert.All(before, f => Assert.Equal(f.Value, File.ReadAllBytes(f.Key)));
    }

    [Fact]
    public void Remove_ANamedClient_TakesOutItsEntryWithABackupAndForgetsIt()
    {
        Run(ClientConfigMode.Write, "codex", "cursor");
        string cursor = McpClient.Find("cursor")!.ConfigFile(Folders);

        ClientConfigReport report = Run(ClientConfigMode.Remove, "cursor");

        Assert.Equal((ClientOutcome.Removed, cursor + Backup), (report.Clients.Single().Outcome, report.Clients.Single().Backup));
        Assert.Empty(JsonNode.Parse(File.ReadAllText(cursor))!["mcpServers"]!.AsObject());
        Assert.Equal(["codex"], Record.All().Select(e => e.Client));
    }

    [Fact]
    public void Remove_WithoutClients_TakesOutOnlyTheRecordedEntries()
    {
        Run(ClientConfigMode.Write, "codex", "gemini-cli");
        string mine = Put("cursor", """{"mcpServers": {"eplan": {"command": "D:\\mine\\eplanmcp.exe"}}}""");

        ClientConfigReport report = Run(ClientConfigMode.Remove);

        Assert.Equal([("codex", ClientOutcome.Removed), ("gemini-cli", ClientOutcome.Removed)], report.Clients.Select(r => (r.Client, r.Outcome)));
        Assert.DoesNotContain("eplan", File.ReadAllText(McpClient.Find("codex")!.ConfigFile(Folders)), StringComparison.Ordinal);
        Assert.Contains(@"D:\\mine", File.ReadAllText(mine), StringComparison.Ordinal);
        Assert.Empty(Record.All());
        Assert.Empty(Run(ClientConfigMode.Remove).Clients);
    }

    [Fact]
    public void Remove_OfAnEntryAlreadyGone_IsUnchangedAndForgotten()
    {
        Run(ClientConfigMode.Write, "qwen-code");
        File.Delete(McpClient.Find("qwen-code")!.ConfigFile(Folders));

        ClientConfigReport report = Run(ClientConfigMode.Remove);

        Assert.Equal(ClientOutcome.Unchanged, report.Clients.Single().Outcome);
        Assert.False(report.Failed);
        Assert.Empty(Record.All());
    }

    [Fact]
    public void Write_ToAnInvalidFile_FailsThatClientAndLeavesItAlone()
    {
        string claude = Put("claude-code", "{\"mcpServers\": ");

        ClientConfigReport report = Run(ClientConfigMode.Write, "claude-code", "codex");

        Assert.Equal([ClientOutcome.Failed, ClientOutcome.Written], report.Clients.Select(r => r.Outcome));
        Assert.Equal(ClientProblem.InvalidFile, report.Clients[0].Problem!.Problem);
        Assert.Equal("{\"mcpServers\": ", File.ReadAllText(claude));
        Assert.False(File.Exists(claude + Backup));
        Assert.Equal(["codex"], Record.All().Select(e => e.Client));
        Assert.True(report.Failed);
    }

    [Theory]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    [InlineData("eplanmcp.exe")]
    [InlineData("")]
    public void Write_NotRunAsEplanmcp_IsRefusedForEveryClient(string executable)
    {
        ClientConfigReport report = new ClientConfigCommand(Folders, new HostDataLocations(Path.Combine(_root, "数据")), executable, _time)
            .Run(new([McpClient.Find("codex")!], ClientConfigMode.DryRun));

        Assert.Equal(ClientProblem.NotEplanmcp, report.Problem!.Problem);
        Assert.Empty(report.Clients);
        Assert.Empty(Files());
    }

    [Fact]
    public void AnUnreadableRecord_FailsTheRecordButNotTheWriteAndStopsRemoveAll()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Record.FilePath)!);
        File.WriteAllText(Record.FilePath, "not json");

        ClientResult written = Run(ClientConfigMode.Write, "kimi-code").Clients.Single();
        ClientConfigReport removeAll = Run(ClientConfigMode.Remove);

        Assert.Equal((ClientOutcome.Written, ClientProblem.RecordFailed), (written.Outcome, written.Problem!.Problem));
        Assert.True(File.Exists(written.File));
        Assert.Equal(ClientProblem.InvalidRecord, removeAll.Problem!.Problem);
        Assert.Equal("not json", File.ReadAllText(Record.FilePath));
    }

    [Fact]
    public void Remove_OfARecordedClientThisVersionDoesNotKnow_FailsThatOne()
    {
        Record.Add(new("windsurf", Path.Combine(Profile, ".codeium", "mcp.json"), "eplan", _time.Now));

        ClientResult result = Run(ClientConfigMode.Remove).Clients.Single();

        Assert.Equal((ClientOutcome.Failed, ClientProblem.UnknownClient, "windsurf"), (result.Outcome, result.Problem!.Problem, result.Problem.Detail));
        Assert.Single(Record.All());
    }

    [Fact]
    public void Write_ClaudeDesktopWithItsPackage_WritesThePackagesCopy()
    {
        string package = Path.Combine(Profile, "AppData", "Local", "Packages", McpClient.ClaudePackageFamily);
        Directory.CreateDirectory(package);

        ClientResult result = Run(ClientConfigMode.Write, "claude-desktop").Clients.Single();

        Assert.Equal(Path.Combine(package, "LocalCache", "Roaming", "Claude", "claude_desktop_config.json"), result.File);
        Assert.True(File.Exists(result.File));
        Assert.False(Directory.Exists(Path.Combine(Profile, "AppData", "Roaming", "Claude")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_PrintsTheReportAndExitsWith1WhenAnyFailed(bool json)
    {
        Put("gemini-cli", "[]");
        using var output = new StringWriter();

        int exit = Command(Exe).Run(new([McpClient.Find("gemini-cli")!, McpClient.Find("opencode")!], ClientConfigMode.Write), output, CultureInfo.GetCultureInfo("en-US"), json);

        Assert.Equal(1, exit);
        if (json)
        {
            JsonNode report = JsonNode.Parse(output.ToString())!;
            Assert.Equal(["failed", "written"], report["clients"]!.AsArray().Select(c => c!["result"]!.GetValue<string>()));
            Assert.Equal("invalid_file", report["clients"]![0]!["problem"]!["code"]!.GetValue<string>());
        }
        else
        {
            Assert.Contains("[failed] gemini-cli", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("[written] opencode", output.ToString(), StringComparison.Ordinal);
        }

        using var again = new StringWriter();
        Assert.Equal(0, Command(Exe).Run(new([McpClient.Find("opencode")!], ClientConfigMode.Write), again, CultureInfo.InvariantCulture, json));
    }

    private ClientConfigCommand Command(string executable) => new(Folders, new HostDataLocations(Path.Combine(_root, "数据")), executable, _time);

    private ClientConfigReport Run(ClientConfigMode mode, params string[] clients) =>
        Command(Exe).Run(new([.. clients.Select(c => McpClient.Find(c)!)], mode));

    private string Put(string client, string text)
    {
        string file = McpClient.Find(client)!.ConfigFile(Folders);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    private List<string> Files() => Directory.Exists(_root) ? [.. Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)] : [];

    private Dictionary<string, byte[]> Contents() => Files().ToDictionary(f => f, File.ReadAllBytes);
}
