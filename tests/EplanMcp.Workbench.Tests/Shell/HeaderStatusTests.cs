using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Shell;

/// <summary>
/// The Header's status (workbench-ui STS-10, STS-11): whether approvals are taken, always in words, with the reason and
/// where questions go instead when not; the waiting count, named in full, which opens the queue.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class HeaderStatusTests(WpfContext wpf)
{
    [Theory]
    [InlineData(ApprovalChannelState.Accepting, "正在接收审批问题", false)]
    [InlineData(ApprovalChannelState.Full, "不接收新问题：已有 50 个在等待，新的问题改由 EPLAN 中的对话框询问", true)]
    [InlineData(ApprovalChannelState.NameTaken, "不接收审批问题：管道名已被占用，审批改由 EPLAN 中的对话框询问", true)]
    public void TheChannelState_IsShownInWords_WithWhereQuestionsGoWhenNotTaken(ApprovalChannelState state, string text, bool explained)
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            window.ShowApprovals(state, 0);

            // In the text itself, not only in the tooltip: the engineer must see where questions go (STS-11).
            var status = (TextBlock)window.FindName("PipeText");
            Assert.Equal(text, status.Text);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Equal(explained, AutomationProperties.GetHelpText(status).Length > 0);
            Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
            Loc.Current.Language = AppLanguage.English;
            Assert.StartsWith("Taking", status.Text, StringComparison.Ordinal);
            Assert.Equal(explained, status.Text.EndsWith("asked in the EPLAN dialog", StringComparison.Ordinal));
        }));
    }

    [Fact]
    public void APlaceAHandshakeReserved_CountsAgainstTheQueue_SoTheHeaderNeverSaysTakenWhileHostsAreTurnedAway()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            var queue = new ApprovalQueue(TimeProvider.System, capacity: 1);
            var status = (TextBlock)window.FindName("PipeText");

            window.ShowQueue(queue, listening: true);
            Assert.Equal("正在接收审批问题", status.Text);
            Assert.True(queue.TryReserve());
            window.ShowQueue(queue, listening: true);
            Assert.StartsWith("不接收新问题", status.Text, StringComparison.Ordinal);
            queue.Release();
            window.ShowQueue(queue, listening: true);
            Assert.Equal("正在接收审批问题", status.Text);
            window.ShowQueue(queue, listening: false);
            Assert.StartsWith("不接收审批问题", status.Text, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void TheWaitingCount_IsNamedInFull_AndOpensTheQueueWithFocusOnItsTitle()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => With(window =>
        {
            var badge = (Button)window.FindName("PendingBadge");
            window.ShowApprovals(ApprovalChannelState.Accepting, 0);
            Assert.Equal(Visibility.Collapsed, badge.Visibility);
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Menu);

            window.ShowApprovals(ApprovalChannelState.Accepting, 3);
            Click(badge);

            Assert.Equal(Visibility.Visible, badge.Visibility);
            Assert.Equal("3 个问题等待审批", AutomationProperties.GetName(badge));
            Assert.Equal(WorkbenchPage.Approvals, window.CurrentPage);
            Assert.Same(((IWorkbenchPage)window.FindName("ApprovalsPage")).Heading, Keyboard.FocusedElement);
        }));
    }
}
