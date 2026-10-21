using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The tray and leaving (NAV-09, STS-09, STS-10, DLG-11): closing hides the window, the tray says how many questions
/// wait, and the exit confirmation defaults to staying.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class TrayAndExitTests(WpfContext wpf)
{
    [Fact]
    public void ClosingTheWindow_OnlyHidesIt_AndItsCloseButtonSaysSo()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            window.HideOnClose = true;
            var close = (Button)window.FindName("CloseButton");

            window.Close();
            WpfContext.Drain();

            Assert.False(window.IsVisible);
            Assert.Equal("关闭到托盘：Workbench 继续接收审批问题。", close.ToolTip);
            window.Restore();
            WpfContext.Drain();
            Assert.True(window.IsVisible);
            window.HideOnClose = false;
        }));
    }

    [Theory]
    [InlineData(0, "", "EplanMcp Workbench", "EplanMcp Workbench")]
    [InlineData(3, "3", "3 questions wait for approval", "3 个问题等待审批")]
    [InlineData(12, "9+", "12 questions wait for approval", "12 个问题等待审批")]
    public void TheTray_ShowsTheCountOnTheIconAndInWords(int pending, string badge, string english, string chinese)
    {
        wpf.RunWithLanguage(AppLanguage.English, () =>
        {
            Assert.Equal(badge, TrayIcon.Badge(pending));
            Assert.Equal(english, TrayIcon.Tip(pending));
            Loc.Current.Language = AppLanguage.Chinese;
            Assert.Equal(chinese, TrayIcon.Tip(pending));
            using var tray = new TrayIcon(() => { }, () => { }) { Pending = pending };
            Assert.Equal(pending, tray.Pending);
        });
    }

    [Theory]
    [InlineData("eplan_page_delete", "3 questions wait for approval · new: eplan_page_delete", "3 个问题等待审批 · 新问题：eplan_page_delete")]
    [InlineData("eplan_page_delete =K1 Line1", "3 questions wait for approval", "3 个问题等待审批")]
    [InlineData("Eplan_Page_Delete", "3 questions wait for approval", "3 个问题等待审批")]
    [InlineData("eplan_\u202Eexe", "3 questions wait for approval", "3 个问题等待审批")]
    [InlineData("项目", "3 questions wait for approval", "3 个问题等待审批")]
    [InlineData("", "3 questions wait for approval", "3 个问题等待审批")]
    public void ANotification_SaysTheCountAndTheTool_AndLeavesOutAnythingThatIsNotAToolName(string tool, string english, string chinese)
    {
        wpf.RunWithLanguage(AppLanguage.English, () =>
        {
            Assert.Equal(english, TrayIcon.ReminderText(3, tool));
            Loc.Current.Language = AppLanguage.Chinese;
            Assert.Equal(chinese, TrayIcon.ReminderText(3, tool));
        });
    }

    [Fact]
    public void AToolName_IsAtMost64LowercaseLettersDigitsOrUnderscores()
    {
        Assert.True(TrayIcon.IsToolName(new string('a', 64)));
        Assert.False(TrayIcon.IsToolName(new string('a', 65)));
        Assert.True(TrayIcon.IsToolName("eplan_2d_export"));
        Assert.False(TrayIcon.IsToolName("eplan-page"));
    }

    [Fact]
    public void TheExitConfirmation_DefaultsToStaying_AndOnlyItsActionExits()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            Loc loc = Loc.Current;
            string message = loc.T("Exit.Message", 2);

            bool? stayed = Ask(window, message, dialog =>
            {
                var safe = (Button)dialog.FindName("SafeButton");
                Assert.Equal("退出 Workbench？", dialog.Title);
                Assert.True(safe.IsDefault && safe.IsCancel);
                Assert.False(((Button)dialog.FindName("ActionButton")).IsDefault);
                Assert.Same(safe, Keyboard.FocusedElement);
                Assert.True(AutomationProperties.GetIsDialog((UIElement)dialog.Content));
                Assert.Equal("取消", AutomationProperties.GetName(safe));
                return safe;
            });
            bool? exited = Ask(window, message, dialog => (Button)dialog.FindName("ActionButton"));

            Assert.Equal("有 2 个问题在等待回答。退出后它们按“已取消”返回给 AI，不执行任何修改。", message);
            Assert.NotEqual(true, stayed);
            Assert.True(exited);
        }));
    }

    /// <summary>Shows the dialog modally and presses the button <paramref name="inspect"/> returns.</summary>
    private static bool? Ask(Window owner, string message, Func<WorkbenchDialog, Button> inspect)
    {
        Loc loc = Loc.Current;
        var dialog = new WorkbenchDialog(loc["Exit.Title"], message, loc["Exit.Cancel"], loc["Exit.Confirm"]) { Owner = owner, ShowActivated = false };
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            Button button = inspect(dialog);
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        });
        return dialog.ShowDialog();
    }
}
