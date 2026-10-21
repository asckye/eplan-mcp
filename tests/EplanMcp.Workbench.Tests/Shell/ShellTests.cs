using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;

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
        wpf.RunWithLanguage(AppLanguage.English, () => WithWindow(window =>
        {
            KeyBinding binding = window.InputBindings.OfType<KeyBinding>().Single(b => b.Key == key && b.Modifiers == ModifierKeys.Control);
            binding.Command.Execute(binding.CommandParameter);
            WpfContext.Drain();

            Assert.Equal(page, window.CurrentPage);
            Assert.Equal($"{english} – EplanMcp Workbench", window.Title);
            Assert.Equal(Visibility.Visible, View(window, page).Visibility);
            Assert.Single(Views(window), v => v.Visibility == Visibility.Visible);
            Assert.True(Item(window, page).IsChecked);
            Assert.Same(((PageView)View(window, page)).Heading, Keyboard.FocusedElement);
            Loc.Current.Language = AppLanguage.Chinese;
            Assert.Equal($"{chinese} – EplanMcp Workbench", window.Title);
        }));
    }

    [Fact]
    public void AMenuItem_ShowsItsPageAndKeepsTheFocus()
    {
        wpf.Run(() => WithWindow(window =>
        {
            RadioButton audit = Item(window, WorkbenchPage.Audit);
            audit.Focus();

            audit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WpfContext.Drain();

            Assert.Equal(WorkbenchPage.Audit, window.CurrentPage);
            Assert.Same(audit, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void TheMenu_IsOneGroupOfFourItems_EachWithItsPositionAndShortcut()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithWindow(window =>
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
    public void TheWindow_FitsTheSmallestWorkAreas()
    {
        wpf.Run(() => WithWindow(window =>
        {
            Assert.True(window.MinWidth <= 960 && window.MinHeight <= 640, $"{window.MinWidth} x {window.MinHeight}");
            Assert.Equal(WorkbenchPage.Approvals, window.CurrentPage);
        }));
    }

    private static void WithWindow(Action<MainWindow> test)
    {
        var window = new MainWindow { ShowActivated = false };
        try
        {
            window.Show();
            WpfContext.Drain();
            test(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static RadioButton Item(MainWindow window, WorkbenchPage page) => (RadioButton)window.FindName(page + "Item");

    private static FrameworkElement View(MainWindow window, WorkbenchPage page) => (FrameworkElement)window.FindName(page + "Page");

    private static IEnumerable<FrameworkElement> Views(MainWindow window) => Enum.GetValues<WorkbenchPage>().Select(p => View(window, p));
}
