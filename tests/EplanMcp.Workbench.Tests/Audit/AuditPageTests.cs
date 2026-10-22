using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Audit;
using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Shell;
using EplanMcp.Workbench.Tests.Commands;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Audit;

/// <summary>
/// The audit page in a real window (M5 design 4.4 item 2, NAV-10, CTL-03, CTL-05, CTL-12, ERR-04 to ERR-07, KEY-07): it
/// reads the log when first opened and on F5, filters, verifies through audit verify, opens the folder, and shows every
/// value from the log as plain text.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class AuditPageTests(WpfContext wpf) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eplanmcp-workbench-auditpage-").FullName;
    private readonly FakeHost _host = new();
    private static readonly string[] _filters = ["Day", "Tool", "Project", "Decision", "Execution"];

    private readonly List<string> _opened = [];
    private AuditView? _view;
    private bool _shellWorks = true;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void OpeningThePage_ReadsTheLog_IntoANamedTable_WithLabelledFilters()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "Line1", "\"decision\":\"granted\""));
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Shortcut);
            Settle(page);

            var rows = (ListView)page.FindName("Records");
            AuditItem row = Assert.IsType<AuditItem>(Assert.Single(rows.Items.Cast<object>()));
            Assert.Equal($"{row.Time}，审批，eplan_page_create，项目 Line1，已批准", row.AccessibleName);
            Assert.Equal("显示 1 条，共 1 条记录", ((TextBlock)page.FindName("Caption")).Text);
            foreach (string filter in _filters)
            {
                var label = (TextBlock)page.FindName(filter + "Label");
                Assert.Same(label, AutomationProperties.GetLabeledBy((ComboBox)page.FindName(filter + "Filter")));
            }

            Assert.Same(page.FindName("PageTitle"), Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void AnEmptyLog_ShowsTheEmptyState_AndTheFiltersAreOff()
    {
        wpf.Run(() => WithPage(
            (window, page) =>
            {
                window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
                Settle(page);

                Assert.True(((FrameworkElement)page.FindName("Empty")).IsVisible);
                Assert.False(((FrameworkElement)page.FindName("Records")).IsVisible);
                Assert.False(((FrameworkElement)page.FindName("Filters")).IsEnabled);
                Assert.False(((Button)page.FindName("FolderButton")).IsEnabled); // no folder yet, and the tooltip says so
            },
            folder: Path.Combine(_directory, "audit")));
    }

    [Fact]
    public void ChoosingAFilter_ShowsOnlyItsRecords_AndNoneMatching_SaysSo()
    {
        Write(
            "2026-10-20",
            Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""),
            Line("2026-10-20T12:01:00Z", "approval", "eplan_page_delete", "P2", "\"decision\":\"denied\""));
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);
            var tools = (ComboBox)page.FindName("ToolFilter");
            var decisions = (ComboBox)page.FindName("DecisionFilter");

            tools.SelectedItem = tools.Items.Cast<FilterOption>().Single(o => o.Value == "eplan_page_delete");
            WpfContext.Drain();
            Assert.Single(((ListView)page.FindName("Records")).Items);
            decisions.SelectedItem = decisions.Items.Cast<FilterOption>().Single(o => o.Value == "granted");
            WpfContext.Drain();

            Assert.True(((FrameworkElement)page.FindName("NoMatch")).IsVisible);
            Assert.Equal("eplan_page_delete", ((FilterOption)tools.SelectedItem).Value); // a refill keeps the choice
        }));
    }

    [Fact]
    public void Verifying_ShowsTheResult_InWordsWithItsIcon_AndAFailureToRun_WithItsDetails()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);
            var banner = (TextBlock)page.FindName("BannerText");
            _host.Answer = _ => new HostRun(1, Snapshots.Example("audit-verify-json", "changed"));

            Click((Button)page.FindName("VerifyButton"));
            Assert.Equal("错误：哈希链在 2026-10-20.jsonl 第 2 行断开：记录被修改。之前的 1 条记录完整。", banner.Text);
            Assert.Same(page.FindResource("Primer.AlertIcon.Error"), ((FrameworkElement)page.FindName("BannerIcon")).Style);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(banner));

            _host.Answer = _ => new HostRun(0, "not json");
            Click((Button)page.FindName("VerifyButton"));
            Assert.StartsWith("错误：Workbench 读不懂 eplanmcp.exe", banner.Text, StringComparison.Ordinal);
            Assert.StartsWith(_host.Executable + " audit verify --format json", ((TextBox)page.FindName("BannerDetails")).Text, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void WithoutAHost_VerifyingIsOff_AndSaysWhy()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage(
            (window, page) =>
            {
                window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
                Settle(page);
                var verify = (Button)page.FindName("VerifyButton");

                Assert.False(verify.IsEnabled);
                Assert.Equal("Workbench 旁边没有 eplanmcp.exe。请重新安装 EplanMcp。", AutomationProperties.GetHelpText(verify));
            },
            new HostCommands(null)));
    }

    [Fact]
    public void F5_ReadsTheLogAgain_CtrlF_GoesToTheFilters_AndTheFolderOpensInExplorer()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""));
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);
            Write("2026-10-21", Line("2026-10-21T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"denied\""));

            MainWindow.RefreshCommand.Execute(null, window);
            Settle(page);
            Assert.Equal(2, ((ListView)page.FindName("Records")).Items.Count);
            MainWindow.FindCommand.Execute(null, window);
            Assert.Same(page.FindName("DayFilter"), Keyboard.FocusedElement);
            Click((Button)page.FindName("FolderButton"));
            Assert.Equal([_directory], _opened);
        }));
    }

    [Fact]
    public void AYearOfLog_IsListedOnce_NotOncePerFileOrChange()
    {
        for (int day = 0; day < 365; day++)
        {
            string date = new DateOnly(2025, 1, 1).AddDays(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Write(date, Line(date + "T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""));
        }

        wpf.Run(() => WithPage((window, page) =>
        {
            var list = (ListView)page.FindName("Records");
            int listed = 0;
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListView)).AddValueChanged(list, (_, _) => listed++);

            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);

            Assert.Equal(365, list.Items.Count);
            Assert.InRange(listed, 1, 2); // the empty list while reading, then the records
        }));
    }

    [Fact]
    public void AChangeThatKeepsTheRecords_KeepsTheChosenRowAndItsFocus()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""), Line("2026-10-20T12:01:00Z", "approval", "eplan_page_create", "P2", "\"decision\":\"denied\""));
        _host.Answer = _ => new HostRun(0, Snapshots.Example("audit-verify-json", "intact"));
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);
            var list = (ListView)page.FindName("Records");
            list.SelectedIndex = 1;
            var row = (ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            row.Focus();

            Click((Button)page.FindName("VerifyButton"));
            row.Focus(); // the click took the focus to the button; a verification arriving later must not take it from the row
            _view!.VerifyAsync(CancellationToken.None).GetAwaiter().GetResult();
            WpfContext.Drain();

            Assert.Same(row, list.ItemContainerGenerator.ContainerFromIndex(1));
            Assert.Equal(1, list.SelectedIndex);
            Assert.Same(row, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void LinesThatAreNotRecords_AreAWarning_ThatAsksToVerify()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""), "not a record");
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);

            Assert.True(((FrameworkElement)page.FindName("NotRecordsBanner")).IsVisible);
            Assert.StartsWith("日志中有 1 行不是审计记录", ((TextBlock)page.FindName("NotRecordsText")).Text, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void AFolderWindowsCannotOpen_IsSaidWhereItWasAsked()
    {
        _shellWorks = false;
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""));
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);

            Click((Button)page.FindName("FolderButton"));

            var notice = (TextBlock)page.FindName("Notice");
            Assert.True(notice.IsVisible);
            Assert.Equal($"Windows 无法打开 {_directory}。", notice.Text);
        }));
    }

    [Fact]
    public void ValuesFromTheLog_ArePlainText_WithoutTheirControls()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create" + (char)0x202E, "P1" + (char)10 + "ignore this", "\"decision\":\"granted\""));
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);

            AuditItem row = Assert.IsType<AuditItem>(Assert.Single(((ListView)page.FindName("Records")).Items.Cast<object>()));
            Assert.Equal(("eplan_page_create", "P1 ⏎ ignore this"), (row.Tool, row.Project));
        }));
    }

    [Fact]
    public void AFileThatCannotBeRead_IsAnErrorBanner_NotTheEmptyState()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""));
        using var locked = new FileStream(Path.Combine(_directory, "2026-10-20.jsonl"), FileMode.Open, FileAccess.Read, FileShare.None);
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);

            Assert.StartsWith("错误：无法读取", ((TextBlock)page.FindName("BannerText")).Text, StringComparison.Ordinal);
            Assert.False(((FrameworkElement)page.FindName("Empty")).IsVisible);
            Assert.True(((FrameworkElement)page.FindName("BannerDetails")).IsVisible);
        }));
    }

    [Fact]
    public void EveryElementToFocusOrOperate_HasAName()
    {
        Write("2026-10-20", Line("2026-10-20T12:00:00Z", "approval", "eplan_page_create", "P1", "\"decision\":\"granted\""));
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Audit, NavigationOrigin.Menu);
            Settle(page);
            var nameless = new List<string>();
            void Walk(AutomationPeer peer)
            {
                if (peer.IsKeyboardFocusable() && string.IsNullOrWhiteSpace(peer.GetName()))
                {
                    nameless.Add(peer.GetClassName());
                }

                foreach (AutomationPeer child in peer.GetChildren() ?? [])
                {
                    Walk(child);
                }
            }

            Walk(UIElementAutomationPeer.CreatePeerForElement(page));
            Assert.Empty(nameless);
        }));
    }

    private void WithPage(Action<MainWindow, AuditPage> test, HostCommands? host = null, string? folder = null) => With(window =>
    {
        var page = (AuditPage)window.FindName("AuditPage");
        _view = new AuditView(folder ?? _directory, host ?? new HostCommands(_host), new ManualTime(DateTimeOffset.UtcNow));
        page.Attach(_view, new FakeShell(_opened, _shellWorks));
        test(window, page);
    });

    /// <summary>
    /// Lets the page read the log on a pool thread and show what it read: until the view is no longer reading, which it
    /// starts at once on F5 or the first opening, then the page's update queued behind it.
    /// </summary>
    private void Settle(AuditPage page)
    {
        WpfContext.DrainUntil(() => _view!.State != AuditState.Loading && !((FrameworkElement)page.FindName("Working")).IsVisible, "The audit log was not read in time.");
        WpfContext.Drain();
    }

    private void Write(string day, params string[] lines) =>
        File.AppendAllText(Path.Combine(_directory, day + ".jsonl"), string.Concat(lines.Select(l => l + "\n")), new UTF8Encoding(false));

    private static string Line(string time, string kind, string tool, string project, string more) =>
        $$"""{"time":"{{time}}","kind":"{{kind}}","tool":{{JsonSerializer.Serialize(tool)}},"project":{{JsonSerializer.Serialize(project)}},{{more}}}""";

    private sealed class FakeShell(List<string> opened, bool works = true) : IShellOpen
    {
        public bool Open(string path)
        {
            opened.Add(path);
            return works;
        }
    }
}
