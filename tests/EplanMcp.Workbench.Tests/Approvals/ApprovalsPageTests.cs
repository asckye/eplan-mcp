using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The approval queue page in a real window (workbench-ui APR-02 to APR-14): the two buttons, the guard, where focus
/// goes and where it never goes, the change list as project data, the Destructive banner.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ApprovalsPageTests(WpfContext wpf) : IDisposable
{
    private readonly ManualTime _clock = new(Asks.Now);
    private ApprovalQueue? _queue;
    private ApprovalQueueView? _view;

    public void Dispose() => _view?.Dispose();

    [Fact]
    public void TheTwoButtons_AreTheSamePlainButton_NeitherDefaultNorCancel_WithoutAccessKeys_RejectFirst()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            Add("r1");
            Button reject = Button(page, "RejectButton");
            Button approve = Button(page, "ApproveButton");

            Assert.All(new[] { reject, approve }, button =>
            {
                Assert.Same(page.FindResource("Workbench.Answer"), button.Style);
                Assert.False(button.IsDefault || button.IsCancel);
                Assert.Empty(AutomationProperties.GetAccessKey(button));
                Assert.Empty(Descendants<AccessText>(button));
            });
            Assert.Equal(("拒绝", "批准"), (AutomationProperties.GetName(reject), AutomationProperties.GetName(approve)));
            Assert.True(reject.TranslatePoint(default, page).X < approve.TranslatePoint(default, page).X);
            Assert.Equal(reject.ActualWidth, approve.ActualWidth);
        }));
    }

    [Fact]
    public void TheButtons_RespondOnly500msAfterTheQuestionRendered()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Question question = Add("r1");
            Button approve = Button(page, "ApproveButton");
            Assert.False(approve.IsEnabled);

            Press(approve);
            _clock.Advance(TimeSpan.FromMilliseconds(499));
            WpfContext.Drain();
            Assert.False(approve.IsEnabled);
            Assert.Null(question.End);
            _clock.Advance(TimeSpan.FromMilliseconds(1));
            WpfContext.Drain();

            Assert.True(approve.IsEnabled);
            Press(approve);
            Assert.Equal(QuestionEnd.Approved, question.End);
        }));
    }

    [Fact]
    public void AfterAnAnswer_FocusIsOnTheNextQuestionsTitle_NeverOnAButton()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1", 60);
            Add("r2", 90);
            Open();
            Button approve = Button(page, "ApproveButton");
            approve.Focus();

            Press(approve);

            Assert.Equal("r2", _view!.Shown!.RequestId);
            Assert.Same(page.FindName("QuestionTitle"), Keyboard.FocusedElement);
            Assert.False(approve.IsEnabled);
        }));
    }

    [Fact]
    public void AnArrival_NeverMovesTheFocus()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            var settings = (Button)window.FindName("SettingsButton");
            settings.Focus();

            Add("r1");

            Assert.Equal("r1", _view!.Shown!.RequestId);
            Assert.Same(settings, Keyboard.FocusedElement);
        }));
    }

    [Fact]
    public void AShownQuestionThatExpires_KeepsTheFocusOnItsTitle_AndItsButtonsGo()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Question question = Add("r1");
            Open();
            Button(page, "RejectButton").Focus();

            _queue!.End(question, QuestionEnd.Expired);
            WpfContext.Drain();

            Assert.Same(page.FindName("QuestionTitle"), Keyboard.FocusedElement);
            Assert.False(((FrameworkElement)page.FindName("Waiting")).IsVisible);
        }));
    }

    [Fact]
    public void EnterOnARow_OnlyOpensTheQuestion_FocusOnItsTitle()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Question question = Add("r1");
            Open();
            var list = (ListBox)page.FindName("Questions");
            list.Focus();

            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            WpfContext.Drain();

            Assert.Same(page.FindName("QuestionTitle"), Keyboard.FocusedElement);
            Assert.Null(question.End);
        }));
    }

    [Fact]
    public void TheChangeList_IsReadOnlyProjectData_Labelled_InTheMonoFont_WithoutLigatures()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            Add("r1", ask: Asks.Write() with { Changes = ["-K1\u202Egnp.exe -> != =="] });
            var changes = (TextBox)page.FindName("Changes");

            Assert.True(changes.IsReadOnly);
            Assert.Equal("-K1gnp.exe -> != ==", changes.Text);
            Assert.Same(page.FindName("ChangesLabel"), AutomationProperties.GetLabeledBy(changes));
            Assert.Same(page.FindResource("Ui.FontMono"), changes.FontFamily);
            Assert.False(Typography.GetStandardLigatures(changes) || Typography.GetContextualLigatures(changes) || Typography.GetContextualAlternates(changes));
        }));
    }

    [Fact]
    public void ADestructiveQuestion_SaysSoAtTheTop_WithTheBackupFolderToSelect()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            Add("r1", ask: Asks.Write(tier: QuestionTier.Destructive, tool: "eplan_page_delete"));
            var backup = (TextBox)page.FindName("BackupFolder");

            Assert.True(backup.IsVisible && backup.IsReadOnly);
            Assert.Equal(@"执行前先备份到 D:\EplanMcp\backups\Line1", backup.Text);
            Assert.Contains(Descendants<TextBlock>(page), t => t.Text == "破坏性操作：执行前先备份项目" && t.IsVisible);
        }));
    }

    [Fact]
    public void TheTitles_AreHeadings_FocusableButNotTabStops_AndTheEmptyStateShowsWhenNothingWaits()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            var empty = (FrameworkElement)page.FindName("Empty");
            Assert.True(empty.IsVisible);

            Add("r1");

            var title = (TextBlock)page.FindName("QuestionTitle");
            var pageTitle = (TextBlock)page.FindName("PageTitle");
            Assert.False(empty.IsVisible);
            Assert.Equal((AutomationHeadingLevel.Level2, AutomationHeadingLevel.Level1), (AutomationProperties.GetHeadingLevel(title), AutomationProperties.GetHeadingLevel(pageTitle)));
            Assert.True(title.Focusable && !KeyboardNavigation.GetIsTabStop(title));
        }));
    }

    [Fact]
    public void OpeningThePageAgain_MakesTheButtonsWaitAgain()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1");
            Open();
            Assert.True(Button(page, "ApproveButton").IsEnabled);

            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Shortcut);
            window.Navigate(WorkbenchPage.Approvals, NavigationOrigin.Shortcut);
            WpfContext.Drain();

            Assert.False(Button(page, "ApproveButton").IsEnabled);
            Open();
            Assert.True(Button(page, "ApproveButton").IsEnabled);
        }));
    }

    private void WithPage(Action<MainWindow, ApprovalsPage> test) => With(window =>
    {
        _queue = new ApprovalQueue(_clock);
        _view = new ApprovalQueueView(_queue, _clock);
        _queue.Changed += q => _view.Update(q);
        window.Attach(_view);
        WpfContext.Drain();
        test(window, (ApprovalsPage)window.FindName("ApprovalsPage"));
    });

    private Question Add(string requestId, int seconds = 120, ApprovalAskParams? ask = null)
    {
        Question question = _queue!.Add((ask ?? Asks.Write(requestId)) with { RequestId = requestId, DeadlineUnixMs = (_clock.GetUtcNow() + TimeSpan.FromSeconds(seconds)).ToUnixTimeMilliseconds() })!;
        WpfContext.Drain();
        return question;
    }

    /// <summary>Lets the shown question render, then the 500 ms pass.</summary>
    private void Open()
    {
        WpfContext.Drain();
        _clock.Advance(ResponseGuard.Delay);
        WpfContext.Drain();
    }

    private static Button Button(ApprovalsPage page, string name) => (Button)page.FindName(name);

    /// <summary>Presses the button as UI Automation and the keyboard do, through its own click handling.</summary>
    private static void Press(Button button)
    {
        if (button.IsEnabled)
        {
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        }

        WpfContext.Drain();
    }

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
