using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using EplanMcp.Workbench.Clients;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Clients;

/// <summary>
/// The client page in a real window (M5 design 4.4 item 3, PRI-01, CTL-07, CTL-13, DLG-12, STS-05, NAV-05): the clients
/// found when it opens, each a named check box; a write only after the engineer confirmed the diff, the safe answer the
/// default; the plugin's two commands to copy.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ClientsPageTests(WpfContext wpf) : IDisposable
{
    private readonly DirectoryInfo _profile = Directory.CreateTempSubdirectory("eplanmcp-workbench-clientspage-");
    private readonly FakeHost _host = new();
    private readonly List<(string Title, string Message, string? Details)> _asked = [];
    private bool _confirm = true;

    public void Dispose() => _profile.Delete(recursive: true);

    [Fact]
    public void OpeningThePage_FindsTheClients_AsNamedCheckBoxes_WithThePluginCommandsToCopy()
    {
        Directory.CreateDirectory(Path.Combine(_profile.FullName, ".codex"));
        _host.Answer = Answer(problem: "plugin_installed");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();

            CheckBox[] boxes = [.. Descendants<CheckBox>(page)];
            Assert.Equal(11, boxes.Length);
            Assert.Equal("Codex，本机已检测到", AutomationProperties.GetName(boxes[2]));
            Assert.True(boxes[0].IsEnabled); // Claude Code with its plugin: no entry is written, but an old one can be removed
            Assert.Equal("已装插件", AutomationProperties.GetHelpText(boxes[0]));
            var command = (TextBox)page.FindName("MarketplaceCommand");
            Assert.Equal((string.Empty, true, Visibility.Collapsed), (command.Text, command.IsReadOnly, command.Visibility));
            Assert.True(((Button)page.FindName("GeneratePluginButton")).IsEnabled);
            Assert.Single(_host.Runs);
        }));
    }

    [Fact]
    public void Writing_PreviewsTheChosenClients_AsksWithTheDiff_AndWritesOnlyWhenConfirmed()
    {
        _host.Answer = Answer();
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();
            Descendants<CheckBox>(page)[1].IsChecked = true; // Claude Desktop
            _confirm = false;

            Click((Button)page.FindName("WriteButton"));
            Assert.Equal(["--dry-run", "--dry-run"], _host.Runs.Select(r => r.Arguments[^3]));
            (string title, string message, string? details) = Assert.Single(_asked);
            Assert.Equal("写入客户端配置？", title);
            Assert.EndsWith("请先完全退出 Claude 桌面版：它运行时会改写自己的配置文件。", message, StringComparison.Ordinal);
            Assert.StartsWith("Claude 桌面版：", details, StringComparison.Ordinal);
            Assert.Contains("+ \"eplan\": {}", details, StringComparison.Ordinal);

            _confirm = true;
            Click((Button)page.FindName("WriteButton"));
            Assert.Equal(["client-config", "claude-desktop", "--format", "json"], _host.Runs[^1].Arguments);
            Assert.Equal("已完成。每张卡片上写明了结果。", ((TextBlock)page.FindName("StatusText")).Text);
            Assert.Contains(Descendants<TextBlock>(page), t => t.Text == "已写入，原文件备份为 C:\\backup");
        }));
    }

    [Fact]
    public void Removing_AsksFirst_ThenRemovesTheChosenClients()
    {
        _host.Answer = Answer();
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();
            Descendants<CheckBox>(page)[9].IsChecked = true; // Cursor

            Click((Button)page.FindName("RemoveButton"));

            Assert.Equal(("移除客户端配置？", "Cursor"), (_asked.Single().Title, _asked.Single().Details));
            Assert.Equal(["client-config", "cursor", "--remove", "--format", "json"], _host.Runs[^1].Arguments);
        }));
    }

    [Fact]
    public void ClaudeCodeWithItsPlugin_IsNotWritten_ButItsOldEntryCanBeRemoved()
    {
        _host.Answer = Answer(problem: "plugin_installed");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();
            Descendants<CheckBox>(page)[0].IsChecked = true;

            Click((Button)page.FindName("WriteButton"));
            Assert.Single(_host.Runs); // the detection only
            Assert.StartsWith("没有要写入的", ((TextBlock)page.FindName("StatusText")).Text, StringComparison.Ordinal);

            Click((Button)page.FindName("RemoveButton"));
            Assert.Equal(["client-config", "claude-code", "--remove", "--format", "json"], _host.Runs[^1].Arguments);
        }));
    }

    [Fact]
    public void NothingChosen_RunsNothing_AndSaysWhy()
    {
        _host.Answer = Answer();
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();

            Click((Button)page.FindName("WriteButton"));

            Assert.Single(_host.Runs);
            Assert.Empty(_asked);
            Assert.Equal("请先选择至少一个客户端。", ((TextBlock)page.FindName("StatusText")).Text);
        }));
    }

    [Fact]
    public void ACommandRefusedAsAWhole_IsAnErrorBanner()
    {
        _host.Answer = _ => new HostRun(1, Snapshots.Example("client-config-json", "refused"));
        wpf.RunWithLanguage(AppLanguage.English, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();

            Assert.True(((FrameworkElement)page.FindName("ErrorBanner")).IsVisible);
            Assert.StartsWith("Error: client-config must run as eplanmcp.exe itself", ((TextBlock)page.FindName("ErrorText")).Text, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void WithoutAHost_NothingRuns_AndTheButtonsSayWhy()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(
            (window, page) =>
            {
                window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
                WpfContext.Drain();

                var write = (Button)page.FindName("WriteButton");
                Assert.False(write.IsEnabled);
                Assert.Equal("Workbench 旁边没有 eplanmcp.exe。请重新安装 EplanMcp。", AutomationProperties.GetHelpText(write));
            },
            new HostCommands(null)));
        Assert.Empty(_host.Runs);
    }

    [Fact]
    public void TheConfirmation_ShowsTheDiffReadOnly_AndDefaultsToTheSafeAnswer()
    {
        wpf.Run(() => With(window =>
        {
            var dialog = new WorkbenchDialog("t", "m", "_Cancel", "_Write", "+ line") { Owner = window, ShowActivated = false };
            dialog.Show();
            WpfContext.Drain();
            var details = (TextBox)dialog.FindName("Details");

            Assert.Equal(("+ line", true, 548.0), (details.Text, details.IsReadOnly, dialog.Width));
            Assert.True(((Button)dialog.FindName("SafeButton")).IsDefault);
            dialog.Close();
        }));
    }

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void GeneratingTheMarketplace_PreviewsAndConfirmsBeforeShowingExactCommands(AppLanguage language)
    {
        string file = Path.Combine(_profile.FullName, "Separate data", "claude-plugin", "eplan-mcp", ".mcp.json");
        _host.Answer = args =>
        {
            HostRun run = Answer()(args);
            if (!args.Contains("--plugin"))
            {
                return run;
            }

            JsonNode report = JsonNode.Parse(run.Output)!;
            report["clients"]![0]!["file"] = file;
            return run with { Output = report.ToJsonString() };
        };
        wpf.RunWithLanguage(language, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Clients, NavigationOrigin.Menu);
            WpfContext.Drain();
            var button = (Button)page.FindName("GeneratePluginButton");
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) && button.Content is null);
            _confirm = false;
            Click(button);
            Assert.Contains("--dry-run", _host.Runs[^1].Arguments);
            Assert.Equal(Visibility.Collapsed, ((TextBox)page.FindName("MarketplaceCommand")).Visibility);
            _confirm = true;
            Click(button);

            Assert.Equal(["client-config", "claude-code", "--plugin", "--format", "json"], _host.Runs[^1].Arguments);
            Assert.Equal("claude plugin marketplace add '" + Path.Combine(_profile.FullName, "Separate data", "claude-plugin") + "'", ((TextBox)page.FindName("MarketplaceCommand")).Text);
            Assert.Equal("claude plugin install eplan-mcp@eplanmcp-local", ((TextBox)page.FindName("InstallCommand")).Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting((TextBlock)page.FindName("StatusText")));
        }));
    }

    private void WithPage(Action<MainWindow, ClientsPage> test, HostCommands? host = null) => With(window =>
    {
        var page = (ClientsPage)window.FindName("ClientsPage");
        page.Confirm = (title, message, safe, action, details) =>
        {
            _asked.Add((title, message, details));
            return _confirm;
        };
        window.AttachClients(new ClientsView(host ?? new HostCommands(_host), _profile.FullName));
        test(window, page);
    });

    /// <summary>Answers each run for the clients it names: a dry run would write, a write wrote, a removal removed.</summary>
    private Func<string[], HostRun> Answer(string? problem = null) => args =>
    {
        string mode = args.Contains("--dry-run") ? "dry_run" : args.Contains("--remove") ? "remove" : "write";
        JsonNode[] clients = [.. args[1..].TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).Select(name => new JsonObject
        {
            ["client"] = name,
            ["file"] = Path.Combine(_profile.FullName, "." + name, "config"),
            ["action"] = mode == "remove" ? "remove" : "write",
            ["result"] = mode switch { "dry_run" => "would_write", "remove" => "removed", _ => "written" },
            ["before"] = null,
            ["after"] = mode == "remove" ? null : "\"eplan\": {}",
            ["backup"] = mode == "dry_run" ? null : "C:\\backup",
            ["notes"] = new JsonArray(),
            ["problem"] = name == "claude-code" && problem is not null ? new JsonObject { ["code"] = problem, ["zh"] = "已装插件", ["en"] = "Plugin installed" } : null,
        })];
        var report = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["mode"] = mode,
            ["server"] = new JsonObject { ["name"] = "eplan", ["command"] = "eplanmcp.exe", ["args"] = new JsonArray("serve") },
            ["problem"] = null,
            ["clients"] = new JsonArray(clients),
        };
        return new HostRun(problem is null ? 0 : 1, report.ToJsonString());
    };

    private static List<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new List<T>();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                found.Add(match);
            }

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }
}
