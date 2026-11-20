using EplanMcp.Host.ClientConfig;
using EplanMcp.Host.Tests.Safety;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>Key-free VM client-config tests confined to a temporary profile.</summary>
[Trait("Size", "Medium")]
public sealed class VmClientConfigCommandTests : IDisposable
{
    private const string Url = "http://192.168.1.42:8766/mcp";
    private readonly string _root = Directory.CreateTempSubdirectory("eplanmcp-vm-config-").FullName;
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 22, 8, 15, 30, TimeSpan.Zero));

    private UserFolders Folders => new(Path.Combine(_root, "profile"), Path.Combine(_root, "roaming"), Path.Combine(_root, "local"), _ => null);
    private HostDataLocations Locations => new(Path.Combine(_root, "data"));
    private ClientRecord Record => ClientRecord.For(Locations);
    private ClientConfigCommand Command => new(Folders, Locations, Path.Combine(_root, "eplanmcp.exe"), _time, () => false, () => null);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Write_EmptyTemporaryProfile_WritesEverySupportedClientKeyFree()
    {
        McpClient[] clients = [.. McpClient.All.Where(RemoteServer.Supports)];
        ClientConfigReport report = Run(clients, ClientConfigMode.Write);

        Assert.False(report.Failed, ClientConfigOutput.Json(report));
        Assert.Equal(10, report.Clients.Count);
        Assert.All(report.Clients, result =>
        {
            Assert.Equal((ClientOutcome.Written, (string?)null), (result.Outcome, result.Backup));
            string content = File.ReadAllText(result.File!);
            Assert.Contains(Url, content, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer ", content, StringComparison.Ordinal);
        });
        Assert.Equal(10, Record.All().Count);
        Assert.All(Record.All(), entry => Assert.Equal("eplan-vm", entry.Entry));
    }

    [Fact]
    public void Write_AllSupportedClients_CoexistsWithLocalAndRemovesCleanly()
    {
        McpClient[] clients = [McpClient.Find("codex")!, McpClient.Find("cursor")!];
        Assert.False(Command.Run(new(clients, ClientConfigMode.Write)).Failed);
        ClientConfigReport written = Run(clients, ClientConfigMode.Write);
        Assert.False(written.Failed, ClientConfigOutput.Json(written));
        _time.Advance(TimeSpan.FromMinutes(1));

        ClientConfigReport removed = Run(clients, ClientConfigMode.Remove);
        Assert.False(removed.Failed);
        Assert.All(removed.Clients, result =>
        {
            Assert.Equal(ClientOutcome.Removed, result.Outcome);
            Assert.DoesNotContain(RemoteServer.Name, File.ReadAllText(result.File!), StringComparison.Ordinal);
            Assert.Contains("eplan", File.ReadAllText(result.File!), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("DryRun")]
    [InlineData("Remove")]
    public void ClaudeDesktop_IsRefusedWithAClearProblemCode(string mode)
    {
        ClientConfigReport report = Run([McpClient.Find("claude-desktop")!], Enum.Parse<ClientConfigMode>(mode));
        Assert.True(report.Failed);
        Assert.Equal(ClientProblem.RemoteUnsupported, report.Clients.Single().Problem!.Problem);
    }

    [Fact]
    public void RemoteEdit_AfterTheFileChanged_IsRefusedBeforeBackup()
    {
        string file = Put("codex", "model = 'original'\n");
        ClientFileChange change = ClientFileChange.Read(file);
        ClientFileEdit edit = CodexToml.Write(change.Text, new RemoteServer(Url));
        File.WriteAllText(file, "model = 'changed'\n");
        Assert.Equal(ClientProblem.FileChanged, Assert.Throws<ClientConfigException>(() => change.Apply(edit.Text!, _time.GetUtcNow())).Problem);
    }

    private ClientConfigReport Run(IReadOnlyList<McpClient> clients, ClientConfigMode mode) =>
        Command.Run(new(clients, mode, Vm: true, Url: mode == ClientConfigMode.Remove ? null : Url));

    private string Put(string client, string text)
    {
        string file = McpClient.Find(client)!.ConfigFile(Folders);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }
}
