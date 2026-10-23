using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Doctor;
using EplanMcp.Host.Tests.Safety;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>Every file, backup and record is confined to an injected temporary profile.</summary>
[Trait("Size", "Medium")]
public sealed class VmClientConfigCommandTests : IDisposable
{
    private const string Url = "http://192.168.1.42:8766/mcp";
    private static readonly string _key = new('A', 43);
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-vm-config-").FullName;
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 22, 8, 15, 30, TimeSpan.Zero));

    private UserFolders Folders => new(Path.Combine(_root, "profile"), Path.Combine(_root, "roaming"), Path.Combine(_root, "local"), _ => null);
    private HostDataLocations Locations => new(Path.Combine(_root, "data"));
    private ClientRecord Record => ClientRecord.For(Locations);
    private ClientConfigCommand Command => new(Folders, Locations, Path.Combine(_root, "eplanmcp.exe"), _time, () => false, () => null);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Write_EmptyTemporaryProfile_WritesEverySupportedClientAndRecordsOnlyVm()
    {
        McpClient[] clients = [.. McpClient.All.Where(RemoteServer.Supports)];
        ClientConfigReport report = Run(clients, ClientConfigMode.Write);

        Assert.False(report.Failed, ClientConfigOutput.Json(report));
        Assert.Equal(10, report.Clients.Count);
        Assert.All(report.Clients, result =>
        {
            Assert.Equal((ClientOutcome.Written, (string?)null), (result.Outcome, result.Backup));
            Assert.Contains("Bearer " + _key, File.ReadAllText(result.File!), StringComparison.Ordinal);
            Assert.Contains("Bearer <key>", result.After, StringComparison.Ordinal);
        });
        Assert.Equal(10, Record.All().Count);
        Assert.DoesNotContain(_key, File.ReadAllText(Record.FilePath), StringComparison.Ordinal);
        Assert.All(Record.All(), entry => Assert.Equal("eplan-vm", entry.Entry));
        AssertSafe(report);
    }

    [Fact]
    public void Write_AllSupportedClients_CoexistsWithLocalAndRecordsBothEntries()
    {
        McpClient[] clients = [.. McpClient.All.Where(RemoteServer.Supports)];
        Assert.False(Command.Run(new(clients, ClientConfigMode.Write)).Failed);
        Dictionary<string, string> originals = clients.ToDictionary(c => c.Name, c => File.ReadAllText(c.ConfigFile(Folders)));

        ClientConfigReport report = Run(clients, ClientConfigMode.Write);

        Assert.False(report.Failed, ClientConfigOutput.Json(report));
        Assert.Equal(20, Record.All().Count);
        Assert.DoesNotContain(_key, File.ReadAllText(Record.FilePath), StringComparison.Ordinal);
        foreach (McpClient client in clients)
        {
            ClientResult result = report.Clients.Single(c => c.Client == client.Name);
            Assert.Equal(ClientOutcome.Written, result.Outcome);
            Assert.Equal(originals[client.Name], File.ReadAllText(result.Backup!));
            string text = File.ReadAllText(result.File!);
            Assert.Contains("Bearer " + _key, text, StringComparison.Ordinal);
            if (client.Format == ClientFormat.CodexToml)
            {
                Assert.StartsWith(originals[client.Name], text, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(originals[client.Name])![client.ServersKey]!["eplan"], JsonNode.Parse(text)![client.ServersKey]!["eplan"]));
            }

            Assert.DoesNotContain(_key, result.After!, StringComparison.Ordinal);
        }

        AssertSafe(report);
        Assert.All(Run(clients, ClientConfigMode.Write).Clients, c => Assert.Equal((ClientOutcome.Unchanged, (string?)null), (c.Outcome, c.Backup)));
    }

    [Fact]
    public void DryRun_Rotation_ShowsRedactedOldAndNewKeysWithoutWriting()
    {
        McpClient[] clients = [McpClient.Find("codex")!, McpClient.Find("cursor")!];
        Run(clients, ClientConfigMode.Write);
        Dictionary<string, byte[]> before = Contents();
        string replacement = new string('B', 42) + "A";
        ClientConfigReport preview = Run(clients, ClientConfigMode.DryRun, replacement + "\r\n");

        Assert.All(preview.Clients, result =>
        {
            Assert.Equal(ClientOutcome.WouldWrite, result.Outcome);
            Assert.Contains("Bearer <key>", result.Before, StringComparison.Ordinal);
            Assert.Contains("Bearer <key>", result.After, StringComparison.Ordinal);
            Assert.DoesNotContain(replacement, result.After!, StringComparison.Ordinal);
            Assert.Null(result.Backup);
        });
        AssertSafe(preview);
        Assert.DoesNotContain(replacement, ClientConfigOutput.Json(preview), StringComparison.Ordinal);
        AssertContents(before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Remove_WithOrWithoutNamedClients_RemovesOnlyVmEntriesWithBackups(bool named)
    {
        McpClient[] clients = [McpClient.Find("codex")!, McpClient.Find("cursor")!];
        Assert.False(Command.Run(new(clients, ClientConfigMode.Write)).Failed);
        ClientConfigReport written = Run(clients, ClientConfigMode.Write);
        Assert.False(written.Failed, ClientConfigOutput.Json(written));
        _time.Advance(TimeSpan.FromMinutes(1));

        ClientConfigReport report = Run(named ? clients : [], ClientConfigMode.Remove);

        Assert.False(report.Failed);
        Assert.Equal(2, report.Clients.Count);
        Assert.All(report.Clients, result =>
        {
            Assert.Equal(ClientOutcome.Removed, result.Outcome);
            Assert.Contains("Bearer " + _key, File.ReadAllText(result.Backup!), StringComparison.Ordinal);
            Assert.DoesNotContain(RemoteServer.Name, File.ReadAllText(result.File!), StringComparison.Ordinal);
            Assert.Contains("eplan", File.ReadAllText(result.File!), StringComparison.Ordinal);
        });
        Assert.All(Record.All(), e => Assert.Equal("eplan", e.Entry));
        AssertSafe(report);
    }

    [Fact]
    public void Remove_UninstallFlow_RemovesRecordedLocalAndVmButKeepsUnrecordedEntries()
    {
        McpClient[] clients = [McpClient.Find("codex")!, McpClient.Find("cursor")!];
        Assert.False(Command.Run(new(clients, ClientConfigMode.Write)).Failed);
        ClientConfigReport written = Run(clients, ClientConfigMode.Write);
        Assert.False(written.Failed, ClientConfigOutput.Json(written));
        string unrecorded = Put("kimi-code", """{"mcpServers":{"eplan-vm":{"url":"mine"}}}""");
        _time.Advance(TimeSpan.FromMinutes(1));
        ClientConfigReport report = Command.Run(new([], ClientConfigMode.Remove));

        Assert.False(report.Failed);
        Assert.Equal(4, report.Clients.Count);
        Assert.Equal(4, report.Clients.Select(c => c.Backup).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(Record.All());
        Assert.Contains("mine", File.ReadAllText(unrecorded), StringComparison.Ordinal);
        Assert.All(clients, client => Assert.DoesNotContain("eplan", File.ReadAllText(client.ConfigFile(Folders)), StringComparison.Ordinal));
        AssertSafe(report);
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("DryRun")]
    [InlineData("Remove")]
    public void ClaudeDesktop_IsRefusedWithAClearProblemCodeAndNoFiles(string mode)
    {
        ClientConfigReport report = Run([McpClient.Find("claude-desktop")!], Enum.Parse<ClientConfigMode>(mode));

        Assert.True(report.Failed);
        Assert.Equal(ClientProblem.RemoteUnsupported, report.Clients.Single().Problem!.Problem);
        Assert.Equal("remote_unsupported", JsonNode.Parse(ClientConfigOutput.Json(report))!["clients"]![0]!["problem"]!["code"]!.GetValue<string>());
        Assert.Empty(Contents());
        AssertSafe(report);
    }

    [Fact]
    public void ClaudeCode_WithLocalPlugin_CanConfigureTheSeparateVmEntry()
    {
        string settings = Path.Combine(Folders.UserProfile, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, """{"enabledPlugins":{"eplan-mcp@eplanmcp-local":true}}""");

        Assert.False(Run([McpClient.Find("claude-code")!], ClientConfigMode.Write).Failed);
        Assert.Contains("eplan-vm", File.ReadAllText(McpClient.Find("claude-code")!.ConfigFile(Folders)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("extra")]
    public void BadStdin_IsReportedAndWritesNothing(string input)
    {
        ClientConfigReport report = Run([McpClient.Find("codex")!], ClientConfigMode.Write, input == "extra" ? _key + "\nmore" : input);
        Assert.Equal(ClientProblem.InvalidVmKey, report.Problem!.Problem);
        Assert.Empty(Contents());
        AssertSafe(report);
    }

    [Fact]
    public void TomlDiagnosticContainingAnOldKey_IsRedactedAndLeavesTheFileAlone()
    {
        string text = "mcp_servers = { eplan-vm = { http_headers = { Authorization = 'Bearer " + _key + "' } } }\n";
        string file = Put("codex", text);
        ClientConfigReport report = Run([McpClient.Find("codex")!], ClientConfigMode.Write);
        Assert.Equal(ClientProblem.UnsupportedToml, report.Clients.Single().Problem!.Problem);
        Assert.DoesNotContain(_key, report.Clients.Single().Problem!.Detail, StringComparison.Ordinal);
        AssertSafe(report);
        Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void RemoteEdit_AfterTheFileChanged_IsRefusedBeforeBackup()
    {
        string file = Put("codex", "model = 'original'\n");
        ClientFileChange change = ClientFileChange.Read(file);
        ClientFileEdit edit = CodexToml.Write(change.Text, new RemoteServer(Url, _key));
        File.WriteAllText(file, "model = 'changed'\n");
        Assert.Equal(ClientProblem.FileChanged, Assert.Throws<ClientConfigException>(() => change.Apply(edit.Text!, _time.GetUtcNow())).Problem);
        Assert.Equal("model = 'changed'\n", File.ReadAllText(file));
        Assert.Single(Contents());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Executable_RedirectsStdin_AndRedactsTextJsonAndStderr(bool json, bool dryRun)
    {
        // The real process refuses to write from inside an MSIX package (design 4.7), such as a Claude Desktop terminal.
        Assert.SkipWhen(DoctorChecks.MsixPackage(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) is not null, "Runs inside an MSIX package.");
        string arguments = "client-config codex --vm --url " + Url + " --key-stdin" + (json ? " --format json" : "") + (dryRun ? " --dry-run" : "");
        (int exit, string stdout, string stderr) = await Execute(arguments, _key + "\r\n");

        Assert.Equal(0, exit);
        Assert.Empty(stderr);
        Assert.DoesNotContain(_key, stdout, StringComparison.Ordinal);
        if (json)
        {
            Assert.Equal(2, JsonNode.Parse(stdout)!["schemaVersion"]!.GetValue<int>());
        }

        string file = Path.Combine(_root, "codex", "config.toml");
        Assert.Equal(!dryRun, File.Exists(file));
        if (!dryRun)
        {
            Assert.Contains("Bearer " + _key, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("extra")]
    public async Task Executable_NoKeyOnStdinOrExtraData_FailsWithoutUsingTheEnvironment(string extra)
    {
        (int exit, string stdout, string stderr) = await Execute("client-config codex --vm --url " + Url + " --key-stdin --format json", extra.Length == 0 ? "" : _key + "\n" + extra);
        Assert.Equal(1, exit);
        Assert.Empty(stderr);
        Assert.DoesNotContain(_key, stdout, StringComparison.Ordinal);
        Assert.Equal("invalid_vm_key", JsonNode.Parse(stdout)!["problem"]!["code"]!.GetValue<string>());
        Assert.Empty(Contents());
    }

    private async Task<(int Exit, string Stdout, string Stderr)> Execute(string arguments, string input)
    {
        // CODEX_HOME is the existing client's temporary-profile seam; this process can only configure Codex.
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "eplanmcp.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["CODEX_HOME"] = Path.Combine(_root, "codex");
        start.Environment["EPLANMCP_HOME"] = Locations.Root;
        start.Environment["EPLANMCP_VM_KEY"] = _key;
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.StandardInput.WriteAsync(input);
        process.StandardInput.Close();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout, await stderr);
    }

    private ClientConfigReport Run(IReadOnlyList<McpClient> clients, ClientConfigMode mode, string? input = null)
    {
        using var stdin = new StringReader(input ?? _key);
        return Command.Run(new(clients, mode, Vm: true, Url: mode == ClientConfigMode.Remove ? null : Url), stdin);
    }

    private string Put(string client, string text)
    {
        string file = McpClient.Find(client)!.ConfigFile(Folders);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    private Dictionary<string, byte[]> Contents() => Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

    private void AssertContents(Dictionary<string, byte[]> before)
    {
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Contents().Keys.Order(StringComparer.Ordinal));
        Assert.All(before, file => Assert.Equal(file.Value, File.ReadAllBytes(file.Key)));
    }

    private static void AssertSafe(ClientConfigReport report)
    {
        Assert.DoesNotContain(_key, ClientConfigOutput.Json(report), StringComparison.Ordinal);
        Assert.DoesNotContain(_key, ClientConfigOutput.Text(report, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(_key, ClientConfigOutput.Text(report, CultureInfo.GetCultureInfo("zh-CN")), StringComparison.Ordinal);
        Assert.DoesNotContain(_key, report.ToString(), StringComparison.Ordinal);
    }
}
