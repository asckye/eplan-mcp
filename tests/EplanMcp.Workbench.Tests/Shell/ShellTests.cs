using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The Automation Studio 4.0 shell: 8-page Activity Rail (Ctrl+1..7, Ctrl+,), single navigation path, focus on page heading,
/// window title, Ctrl+Tab cycling, and minimum size.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ShellTests(WpfContext wpf)
{
    public static TheoryData<WorkbenchPage, Key, string, string> Pages => new()
    {
        { WorkbenchPage.Project, Key.D1, "Project", "项目工作区" },
        { WorkbenchPage.Approvals, Key.D2, "Approvals", "协作与审批" },
        { WorkbenchPage.Mcp, Key.D3, "MCP Server", "MCP 服务" },
        { WorkbenchPage.Clients, Key.D4, "AI Clients", "AI 客户端" },
        { WorkbenchPage.Status, Key.D5, "Checks", "环境检查" },
        { WorkbenchPage.Audit, Key.D6, "Audit", "审计记录" },
        { WorkbenchPage.Logs, Key.D7, "Logs", "运行日志" },
        { WorkbenchPage.Settings, Key.OemComma, "Settings", "偏好与安全" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void CtrlShortcut_ShowsThePage_NamesItInTheTitle_AndFocusesItsHeading(WorkbenchPage page, Key key, string english, string chinese)
    {
        wpf.RunWithLanguage(AppLanguage.English, () => With(window =>
        {
            KeyBinding binding = window.InputBindings.OfType<KeyBinding>().Single(b => b.Key == key && b.Modifiers == ModifierKeys.Control);
            binding.Command.Execute(binding.CommandParameter);
            WpfContext.Drain();

            Assert.Equal(page, window.CurrentPage);
            Assert.Equal($"{english} – EplanMcp Workbench", window.Title);
            Assert.Equal(Visibility.Visible, View(window, page).Visibility);
            Assert.Single(Views(window), v => v.Visibility == Visibility.Visible);
            Assert.True(Item(window, page).IsChecked);
            Assert.Same(((IWorkbenchPage)View(window, page)).Heading, Keyboard.FocusedElement);
            Loc.Current.Language = AppLanguage.Chinese;
            Assert.Equal($"{chinese} – EplanMcp Workbench", window.Title);
        }));
    }

    [Fact]
    public void AMenuItem_ShowsItsPageAndKeepsTheFocus()
    {
        wpf.Run(() => With(window =>
        {
            RadioButton audit = Item(window, WorkbenchPage.Audit);
            audit.Focus();

            Click(audit);

            Assert.Equal(WorkbenchPage.Audit, window.CurrentPage);
            Assert.Same(audit, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void CtrlTab_CyclesThroughThePagesBothWays()
    {
        wpf.Run(() => With(window =>
        {
            Assert.Equal(WorkbenchPage.Approvals, window.CurrentPage);
            MainWindow.CycleCommand.Execute("-1", window);
            Assert.Equal(WorkbenchPage.Project, window.CurrentPage);
            MainWindow.CycleCommand.Execute("-1", window);
            Assert.Equal(WorkbenchPage.Settings, window.CurrentPage);
            MainWindow.CycleCommand.Execute("1", window);
            Assert.Equal(WorkbenchPage.Project, window.CurrentPage);
        }));
    }

    [Fact]
    public void TheActivityRail_HasEightItems_EachWithItsPositionAndShortcut()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            RadioButton[] items = [.. Enum.GetValues<WorkbenchPage>().Select(p => Item(window, p))];
            string[] shortcuts = ["Ctrl+1", "Ctrl+2", "Ctrl+3", "Ctrl+4", "Ctrl+5", "Ctrl+6", "Ctrl+7", "Ctrl+,"];

            Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation((DependencyObject)window.FindName("Menu")));
            for (int i = 0; i < items.Length; i++)
            {
                Assert.Equal((i + 1, 8, shortcuts[i]), (AutomationProperties.GetPositionInSet(items[i]), AutomationProperties.GetSizeOfSet(items[i]), AutomationProperties.GetAcceleratorKey(items[i])));
                Assert.Equal($"{AutomationProperties.GetName(items[i])}（{shortcuts[i]}）", items[i].ToolTip);
            }
        }));
    }

    [Fact]
    public void TheWindow_FitsTheSmallestWorkAreas()
    {
        wpf.Run(() => With(window =>
        {
            Assert.True(window.MinWidth <= 960 && window.MinHeight <= 640, $"{window.MinWidth} x {window.MinHeight}");
            Assert.Equal(WorkbenchPage.Approvals, window.CurrentPage);
        }));
    }

    private static RadioButton Item(MainWindow window, WorkbenchPage page) => (RadioButton)window.FindName(page + "Item");

    private static FrameworkElement View(MainWindow window, WorkbenchPage page) => (FrameworkElement)window.FindName(page + "Page");

    private static IEnumerable<FrameworkElement> Views(MainWindow window) => Enum.GetValues<WorkbenchPage>().Select(p => View(window, p));
}
