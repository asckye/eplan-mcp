using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The shell (workbench-ui 3.1, section 7, NAV-*, KEY-07): one way to change the page, where focus goes, the window
/// title, the Sider's breakpoint, the menu as one group, and the minimum size.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ShellTests(WpfContext wpf)
{
    public static TheoryData<WorkbenchPage, Key, string, string> Pages => new()
    {
        { WorkbenchPage.Approvals, Key.D1, "Approvals", "审批队列" },
        { WorkbenchPage.Audit, Key.D2, "Audit", "审计" },
        { WorkbenchPage.Status, Key.D3, "Status", "状态" },
        { WorkbenchPage.Clients, Key.D4, "MCP and clients", "MCP 与客户端" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void CtrlAndANumber_ShowsThePage_NamesItInTheTitle_AndFocusesItsHeading(WorkbenchPage page, Key key, string english, string chinese)
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
            MainWindow.CycleCommand.Execute("-1", window);
            Assert.Equal(WorkbenchPage.Clients, window.CurrentPage);
            MainWindow.CycleCommand.Execute("1", window);
            MainWindow.CycleCommand.Execute("1", window);
            Assert.Equal(WorkbenchPage.Audit, window.CurrentPage);
        }));
    }

    [Fact]
    public void TheMenu_IsOneGroupOfFourItems_EachWithItsPositionAndShortcut()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            RadioButton[] items = [.. Enum.GetValues<WorkbenchPage>().Select(p => Item(window, p))];

            Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation((DependencyObject)items[0].Parent));
            for (int i = 0; i < items.Length; i++)
            {
                Assert.Equal((i + 1, 4, $"Ctrl+{i + 1}"), (AutomationProperties.GetPositionInSet(items[i]), AutomationProperties.GetSizeOfSet(items[i]), AutomationProperties.GetAcceleratorKey(items[i])));
                Assert.Equal($"{AutomationProperties.GetName(items[i])}（Ctrl+{i + 1}）", items[i].ToolTip);
            }
        }));
    }

    [Fact]
    public void TheSider_FollowsTheBreakpoint_UntilChosenByHand_AndAgainAfterCrossingIt()
    {
        wpf.Run(() => With(window =>
        {
            var sider = (FrameworkElement)window.FindName("Sider");
            var toggle = (Button)window.FindName("SiderToggle");

            Resize(window, 1200);
            Assert.Equal((216d, false), (sider.Width, Ux.GetCompact(sider)));
            Resize(window, 980);
            Assert.Equal((56d, true), (sider.Width, Ux.GetCompact(sider)));
            Click(toggle);
            Resize(window, 970);
            Assert.Equal(216d, sider.Width);
            Resize(window, 1100);
            Resize(window, 970);
            Assert.Equal(56d, sider.Width);
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
