using System.Text.Json.Nodes;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Localization;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Clients;

/// <summary>
/// The client configuration wizard's logic (M5 design 4.4 item 3, 4.11, Q7, DLG-12): the eleven clients found by one dry
/// run, the chosen ones previewed as a plain diff and then written or removed by the Host, each result on its card.
/// In the WPF group because the catalogue's language is one per process.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ClientsViewTests(WpfContext wpf) : IDisposable
{
    private readonly DirectoryInfo _profile = Directory.CreateTempSubdirectory("eplanmcp-workbench-clients-");
    private readonly FakeHost _host = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _profile.Delete(recursive: true);

    [Fact]
    public async Task OneDryRunOfAllEleven_TellsWhichThisComputerHas_ByTheirFileOrItsFolder()
    {
        File.WriteAllText(Path.Combine(_profile.FullName, ".claude.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_profile.FullName, ".codex"));
        _host.Answer = args => new HostRun(0, Report("dry_run", args[1..^3].Select(name => Client(name, FileOf(name), "would_write")).ToArray()));
        ClientsView view = View();

        await view.DetectAsync(Token);

        Assert.Equal(["client-config", .. ClientsView.Names, "--dry-run", "--format", "json"], Assert.Single(_host.Runs).Arguments);
        Assert.Equal(["claude-code", "codex"], view.Cards.Where(c => c.Detected).Select(c => c.Name));
        Assert.Equal(ClientsState.Ready, view.State);
    }

    [Fact]
    public async Task OnlyTheChosenClients_ArePreviewedWrittenOrRemoved_AndNothingRunsForNone()
    {
        _host.Answer = args => new HostRun(0, Report("dry_run", Client("cursor", FileOf("cursor"), "would_write")));
        ClientsView view = View();
        await view.PreviewAsync(Token);
        Assert.Empty(_host.Runs);

        view.Cards.Single(c => c.Name == "cursor").IsSelected = true;
        await view.PreviewAsync(Token);
        await view.ApplyAsync(ClientConfigMode.Write, Token);
        await view.ApplyAsync(ClientConfigMode.Remove, Token);

        Assert.Equal(
            [["client-config", "cursor", "--dry-run", "--format", "json"], ["client-config", "cursor", "--format", "json"], ["client-config", "cursor", "--remove", "--format", "json"]],
            _host.Runs.Select(r => r.Arguments));
    }

    [Fact]
    public async Task ThePreview_IsAPlainDiff_OldLinesMinus_NewLinesPlus_ThenNotesAndProblems()
    {
        _host.Answer = _ => new HostRun(1, Report(
            "dry_run",
            Client("codex", FileOf("codex"), "would_write", before: "[mcp_servers.eplan]\ncommand = \"old\"", after: "[mcp_servers.eplan]\ncommand = \"new‮\""),
            Client("vscode-copilot", FileOf("vscode-copilot"), "would_write", after: "\"eplan\": {}", note: true)));
        ClientsView view = View();
        foreach (ClientCard card in view.Cards.Where(c => c.Name is "codex" or "vscode-copilot"))
        {
            card.IsSelected = true;
        }

        await view.PreviewAsync(Token);

        wpf.RunWithLanguage(AppLanguage.Chinese, () => Assert.Equal(
            string.Join(
                Environment.NewLine,
                $"Codex：{FileOf("codex")}",
                "− [mcp_servers.eplan]",
                "− command = \"old\"",
                "+ [mcp_servers.eplan]",
                "+ command = \"new\"",
                string.Empty,
                $"VS Code Copilot：{FileOf("vscode-copilot")}",
                "+ \"eplan\": {}",
                "! 改写会丢弃注释"),
            view.Diff()));
    }

    [Fact]
    public async Task EachCard_SaysWhatItsWriteDid_WithTheBackup_OrWhyNot()
    {
        _host.Answer = _ => new HostRun(1, Report(
            "write",
            Client("codex", FileOf("codex"), "written", backup: FileOf("codex") + ".bak_1"),
            Client("gemini-cli", FileOf("gemini-cli"), "unchanged"),
            Client("claude-code", FileOf("claude-code"), "failed", problem: "plugin_installed")));
        ClientsView view = View();
        foreach (ClientCard card in view.Cards.Where(c => c.Name is "codex" or "gemini-cli" or "claude-code"))
        {
            card.IsSelected = true;
        }

        await view.ApplyAsync(ClientConfigMode.Write, Token);

        wpf.RunWithLanguage(AppLanguage.Chinese, () => Assert.Equal(
            [$"已写入，原文件备份为 {FileOf("codex")}.bak_1", "已是最新，没有改动", "未完成：已装插件"],
            view.Cards.Where(c => c.Result is not null).OrderBy(c => c.Name == "claude-code").ThenBy(c => c.Name).Select(c => c.ResultText)));
    }

    [Fact]
    public async Task AClaudeCodeWithThePlugin_IsSaidSo_FromTheDryRun()
    {
        _host.Answer = _ => new HostRun(1, Report("dry_run", Client("claude-code", FileOf("claude-code"), "failed", problem: "plugin_installed")));
        ClientsView view = View();

        await view.DetectAsync(Token);

        ClientCard claude = view.Cards[0];
        Assert.True(claude.PluginInstalled);
        wpf.RunWithLanguage(AppLanguage.Chinese, () => Assert.Equal(("已装插件", "Claude Code，本机未检测到"), (claude.Problem, claude.AccessibleName)));
    }

    [Fact]
    public async Task ACommandRefusedAsAWhole_OrWithoutAResult_IsAnError()
    {
        _host.Answer = _ => new HostRun(1, Snapshots.Example("client-config-json", "refused"));
        ClientsView view = View();
        await view.DetectAsync(Token);
        Assert.Equal(ClientsState.Failed, view.State);
        wpf.RunWithLanguage(AppLanguage.English, () => Assert.StartsWith("client-config must run as eplanmcp.exe itself", view.Error, StringComparison.Ordinal));

        _host.Answer = _ => new HostRun(2, string.Empty);
        await view.DetectAsync(Token);
        Assert.Contains("client-config", view.ErrorDetails, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryClientAndResult_HasItsText()
    {
        string[] keys =
        [
            .. ClientsView.Names.Select(n => "Clients.Name." + ClientsView.Key(n)),
            .. Enum.GetNames<ClientResultKind>().Select(k => "Clients.Result." + k),
        ];

        Assert.All(keys, k => Assert.True(Catalogue.English.ContainsKey(k), k));
        Assert.Equal(11, ClientsView.Names.Length);
    }

    private ClientsView View() => new(new HostCommands(_host), _profile.FullName);

    /// <summary>Where each client's file is for the tests: Claude Code's in the profile itself, the others in a folder of their own.</summary>
    private string FileOf(string client) => client == "claude-code"
        ? Path.Combine(_profile.FullName, ".claude.json")
        : Path.Combine(_profile.FullName, "." + client, "config");

    private static string Report(string mode, params JsonObject[] clients) => new JsonObject
    {
        ["schemaVersion"] = 1,
        ["mode"] = mode,
        ["server"] = new JsonObject { ["name"] = "eplan", ["command"] = "eplanmcp.exe", ["args"] = new JsonArray("serve") },
        ["problem"] = null,
        ["clients"] = new JsonArray(clients),
    }.ToJsonString();

    private static JsonObject Client(string name, string file, string result, string? before = null, string? after = null, string? backup = null, string? problem = null, bool note = false) => new()
    {
        ["client"] = name,
        ["file"] = file,
        ["action"] = result is "removed" ? "remove" : "write",
        ["result"] = result,
        ["before"] = before,
        ["after"] = after,
        ["backup"] = backup,
        ["notes"] = note ? new JsonArray(new JsonObject { ["code"] = "comments_dropped", ["zh"] = "改写会丢弃注释", ["en"] = "Rewriting drops comments" }) : new JsonArray(),
        ["problem"] = problem is null ? null : new JsonObject { ["code"] = problem, ["zh"] = "已装插件", ["en"] = "The plugin is installed" },
    };
}
