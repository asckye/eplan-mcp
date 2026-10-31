using System.Reflection;
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
/// goes and where it never goes, the change list as project data, the Destructive banner, the answered group, the narrow
/// layout, the window coming back, the reminders and F6.
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

    [Theory]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Chinese)]
    public void TheTwoButtons_AreTheSameSize_InEitherLanguage_ForAnApprovalAndForARelease(AppLanguage language)
    {
        wpf.RunWithLanguage(language, () => WithPage((window, page) =>
        {
            Button reject = Button(page, "RejectButton");
            Button approve = Button(page, "ApproveButton");
            Add("r1");
            Question release = Add("r2", 150, Asks.Release("r2"));
            var sizes = new List<string>();
            foreach (QuestionItem item in _view!.Pending.ToList())
            {
                _view.Select(item);
                WpfContext.Drain();

                // English "Checked, continue" is longer than "Keep suspended": both take the longer one's size.
                Assert.Equal((reject.ActualWidth, reject.ActualHeight), (approve.ActualWidth, approve.ActualHeight));
                Assert.All(new[] { reject, approve }, b => Assert.True(Descendants<TextBlock>(b).Single().ActualWidth <= b.ActualWidth - b.Padding.Left - b.Padding.Right + 0.5));
                sizes.Add($"{item.RejectLabel}/{item.ApproveLabel}");
            }

            Assert.Equal(2, sizes.Distinct().Count()); // an approval and a release were both measured
            Assert.Same(release, _view.Shown!.Question);
        }));
    }

    [Fact]
    public void AnUnknownOutcome_ShowsTheWarningIcon_AndTheWarningColour_ADoneOneNeither()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            Question unknown = Add("r1");
            Question done = Add("r2", 150);
            _queue!.Answer(unknown, approve: true);
            _queue.Answer(done, approve: true);
            _queue.Report(unknown, WriteExecution.Unknown, "OUTCOME_UNKNOWN");
            _queue.Report(done, WriteExecution.Committed, null);
            WpfContext.Drain();
            var icon = (FrameworkElement)page.FindName("OutcomeWarning");
            var text = (TextBlock)page.FindName("OutcomeText");

            window.Open(Item(unknown));
            WpfContext.Drain();
            Assert.True(icon.IsVisible);
            Assert.Equal(page.FindResource("Primer.warn"), text.Foreground);
            Assert.StartsWith("结果未知：写入已暂停", text.Text, StringComparison.Ordinal);
            var rows = (ListBox)page.FindName("Questions");
            Assert.Single(Descendants<System.Windows.Shapes.Path>(rows), p => p.IsVisible && p.Style == page.FindResource("Primer.AlertIcon.Warning")); // the row of r1

            window.Open(Item(done));
            WpfContext.Drain();
            Assert.False(icon.IsVisible);
            Assert.Equal(page.FindResource("Primer.text"), text.Foreground);
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

            Assert.Throws<ElementNotEnabledException>(() => Invoke(approve));
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
            Focus(window, approve);

            Press(approve);

            Assert.Equal("r2", _view!.Shown!.RequestId);
            Assert.Same(page.FindName("QuestionTitle"), Focused(window));
            Assert.False(approve.IsEnabled);
            Assert.Equal("1", ((Button)window.FindName("PendingBadge")).Content); // the Header followed
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnArrival_NeverMovesTheFocus(bool loseKeyboardFocus)
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            var settings = (Button)window.FindName("SettingsButton");
            Focus(window, settings);

            Add("r1");
            if (loseKeyboardFocus)
            {
                Keyboard.ClearFocus();
                Assert.Null(Keyboard.FocusedElement);
            }

            Assert.Equal("r1", _view!.Shown!.RequestId);
            Assert.Same(settings, Focused(window));
        }));
    }

    [Fact]
    public void AShownQuestionThatExpires_KeepsTheFocusOnItsTitle_AndItsButtonsGo()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Question question = Add("r1");
            Open();
            Focus(window, Button(page, "RejectButton"));

            _queue!.End(question, QuestionEnd.Expired);
            WpfContext.Drain();

            Assert.Same(page.FindName("QuestionTitle"), Focused(window));
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
            Focus(window, list);

            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(list), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            WpfContext.Drain();

            Assert.Same(page.FindName("QuestionTitle"), Focused(window));
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

    [Fact]
    public void AnAnsweredQuestion_IsInTheAnsweredGroup_WithItsResult_AndCannotBeAnsweredThere()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithPage((window, page) =>
        {
            Question question = Add("r1");
            Open();
            Press(Button(page, "ApproveButton"));
            var waiting = (RadioButton)page.FindName("WaitingGroup");
            var answered = (RadioButton)page.FindName("AnsweredGroup");
            Assert.Equal(("等待中（0）", "已回答（1）"), ((string)waiting.Content, (string)answered.Content));
            Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation((DependencyObject)page.FindName("Groups")));

            answered.IsChecked = true;
            var list = (ListBox)page.FindName("Questions");
            list.SelectedIndex = 0;
            WpfContext.Drain();

            Assert.Equal("已回答的问题", AutomationProperties.GetName(list));
            Assert.Same(question, _view!.Shown!.Question);
            Assert.Equal("已批准", _view.Shown.Result);
            Assert.False(((FrameworkElement)page.FindName("Waiting")).IsVisible);
            Assert.False(_view.Answer(approve: true));
            Assert.False(((FrameworkElement)page.FindName("Empty")).IsVisible);
        }));
    }

    [Fact]
    public void InANarrowPage_TheDetailGoesBelowTheList_BothTheFullWidth_AndBackWhenWide()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1");
            var detail = (FrameworkElement)page.FindName("DetailArea");
            var list = (FrameworkElement)page.FindName("Questions");

            Resize(window, 1000); // the Sider stays wide, the page is under 760
            Assert.True(page.ActualWidth < 760);
            Assert.Equal((2, 0, 2, 2), (Grid.GetRow(detail), Grid.GetColumn(detail), Grid.GetColumnSpan(detail), Grid.GetColumnSpan(list)));
            Assert.True(detail.TranslatePoint(default, page).Y >= list.TranslatePoint(new Point(0, list.ActualHeight), page).Y);
            Assert.True(detail.ActualHeight > list.ActualHeight);

            Resize(window, 1400);
            Assert.Equal((1, 1, 1, 1), (Grid.GetRow(detail), Grid.GetColumn(detail), Grid.GetColumnSpan(detail), Grid.GetColumnSpan(list)));
        }));
    }

    [Fact]
    public void TheWindowComingBack_FromHiddenOrMinimized_MakesTheButtonsWaitAgain()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1");
            Open();
            Button approve = Button(page, "ApproveButton");

            window.Hide();
            window.Show();
            WpfContext.Drain();
            Assert.False(approve.IsEnabled);
            Open();
            Assert.True(approve.IsEnabled);

            window.WindowState = WindowState.Minimized;
            WpfContext.Drain();
            window.WindowState = WindowState.Normal;
            WpfContext.Drain();
            Assert.False(approve.IsEnabled);
        }));
    }

    [Fact]
    public void ActivatingTheWindow_TakesFocusOffTheButtons_ToTheTitle_ButLeavesItElsewhere()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1");
            Add("r2", 150);
            Open();
            Focus(window, Button(page, "ApproveButton"));

            Activated(window);
            Assert.Same(page.FindName("QuestionTitle"), Focused(window));

            var list = (ListBox)page.FindName("Questions");
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            Focus(window, row);
            Activated(window);
            Assert.Same(row, Focused(window));
            var menu = (UIElement)window.FindName("ApprovalsItem");
            Focus(window, menu);
            Activated(window);
            Assert.Same(menu, Focused(window));
        }));
    }

    [Fact]
    public void AnArrival_InAHiddenWindow_AsksOnlyForANotification_TheWindowStaysHiddenOnItsPage()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            var notified = new List<QuestionItem>();
            window.NotificationWanted += notified.Add;
            Add("r0");
            Assert.Empty(notified); // shown, not minimized: the taskbar flash is all

            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Shortcut);
            window.Hide();
            // IsActive is activation within the window's own thread, which the focus at startup gave it. Hiding the
            // window from the foreground, as the engineer does, passes it on; a test run that is not in the foreground
            // keeps it on the hidden window. So the arrival is checked for activating the window, not IsActive for false.
            int activated = 0;
            window.Activated += (_, _) => activated++;
            Add("r1", 150);

            Assert.Equal("r1", Assert.Single(notified).RequestId);
            Assert.False(window.IsVisible || window.Topmost);
            Assert.Equal(0, activated);
            Assert.Equal(WorkbenchPage.Status, window.CurrentPage);
        }));
    }

    [Theory]
    [InlineData(true, false, true, Reminder.None)]
    [InlineData(true, false, false, Reminder.Flash)]
    [InlineData(true, true, false, Reminder.Flash | Reminder.Notify)]
    [InlineData(false, false, false, Reminder.Flash | Reminder.Notify)]
    public void AReminder_FlashesWhenNotActive_AndNotifiesWhenHiddenOrMinimized(bool visible, bool minimized, bool active, Reminder expected) =>
        Assert.Equal(expected, MainWindow.ReminderFor(visible, minimized, active));

    [Fact]
    public void OpeningFromANotification_ShowsTheQueueWithThatQuestion_FocusOnItsTitle_ButtonsWaiting()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1");
            Question second = Add("r2", 150);
            Open();
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Shortcut);
            window.Hide();
            // Check both the focus request and the window's logical focus, even if the desktop takes focus back.
            var asked = new List<object>();
            window.AddHandler(
                Keyboard.KeyboardInputProviderAcquireFocusEvent,
                new KeyboardInputProviderAcquireFocusEventHandler((_, e) => asked.Add(e.OriginalSource)),
                handledEventsToo: true);

            window.Open(Item(second));
            WpfContext.Drain();

            object title = page.FindName("QuestionTitle");
            Assert.True(window.IsVisible);
            Assert.Equal(WorkbenchPage.Approvals, window.CurrentPage);
            Assert.Same(second, _view!.Shown!.Question);
            Assert.Same(title, asked.LastOrDefault());
            Assert.Same(title, Focused(window));

            Assert.False(Button(page, "ApproveButton").IsEnabled);
        }));
    }

    [Fact]
    public void OpeningAQuestionThatEnded_ShowsItAnswered_AndOneThatIsGone_ChangesNothing()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Question first = Add("r1");
            Question second = Add("r2", 150);
            _queue!.End(second, QuestionEnd.Expired);
            WpfContext.Drain();

            window.Open(new QuestionItem(new ApprovalQueue(_clock).Add(Asks.Write("gone"))!, _clock));
            WpfContext.Drain();
            Assert.Same(first, _view!.Shown!.Question);

            window.Open(Item(second));
            WpfContext.Drain();
            Assert.Same(second, _view.Shown!.Question);
            Assert.True(((RadioButton)page.FindName("AnsweredGroup")).IsChecked);
            Assert.False(_view.CanAnswer);
        }));
    }

    [Fact]
    public void OpeningFromTheTray_WithNothingWaiting_StaysOnThePage()
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            window.Navigate(WorkbenchPage.Status, NavigationOrigin.Shortcut);
            window.Hide();

            window.Open(null);
            WpfContext.Drain();

            Assert.True(window.IsVisible);
            Assert.Equal(WorkbenchPage.Status, window.CurrentPage);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void F6_GoesRoundTheRegions_BothWays_NeverToAnAnswerButton_AndNotUnderTheSettingsDrawer(bool loseKeyboardFocus)
    {
        wpf.Run(() => WithPage((window, page) =>
        {
            Add("r1");
            Open();
            Focus(window, (UIElement)window.FindName("SettingsButton")); // in the Sider
            var stops = new List<object>();
            for (int i = 0; i < 5; i++)
            {
                MoveRegion("1");
                stops.Add(Focused(window));
            }

            // The Header's stop is the waiting count, shown as a question waits.
            var list = (ListBox)page.FindName("Questions");
            Assert.Equal(
                [window.FindName("PendingBadge"), list.ItemContainerGenerator.ContainerFromIndex(0), page.FindName("QuestionTitle"), window.FindName("ApprovalsItem"), window.FindName("PendingBadge")],
                stops);
            MoveRegion("-1");
            Assert.Same(window.FindName("ApprovalsItem"), Focused(window));
            MoveRegion("-1");
            Assert.Same(page.FindName("QuestionTitle"), Focused(window));
            Assert.Contains(window.InputBindings.OfType<KeyBinding>(), b => b.Key == Key.F6 && b.Modifiers == ModifierKeys.Shift && b.Command == MainWindow.RegionCommand);

            window.OpenSettings();
            Assert.False(MainWindow.RegionCommand.CanExecute("1", window));
            window.CloseSettings();

            void MoveRegion(string direction)
            {
                // F6 reads IsKeyboardFocusWithin. Restore the saved logical stop before executing, rather than
                // letting a desktop deactivation turn every step into the first region.
                Focus(window, Focused(window));
                MainWindow.RegionCommand.Execute(direction, window);
                WpfContext.Drain();
                if (loseKeyboardFocus)
                {
                    Keyboard.ClearFocus();
                    Assert.Null(Keyboard.FocusedElement);
                }
            }
        }));
    }

    [Fact]
    public void ThePage_FollowsTheLanguageWhileItsWindowIsOpen_AndLetsGoWhenItCloses()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () =>
        {
            var window = new MainWindow { ShowActivated = false };
            window.Show();
            WpfContext.Drain();
            var group = (RadioButton)((ApprovalsPage)window.FindName("ApprovalsPage")).FindName("WaitingGroup");
            Loc.Current.Language = AppLanguage.English;
            string english = (string)group.Content;
            Loc.Current.Language = AppLanguage.Chinese;
            Assert.NotEqual(english, group.Content);

            window.Close();
            WpfContext.Drain();
            Loc.Current.Language = AppLanguage.English;

            Assert.NotEqual(english, group.Content); // no handler of the closed page is left on the catalogue
        });
    }

    private void WithPage(Action<MainWindow, ApprovalsPage> test) => With(window =>
    {
        _queue = new ApprovalQueue(_clock);
        _view = new ApprovalQueueView(_queue, _clock);
        _view.Follow(window.Dispatcher, () => window.ShowQueue(_queue, listening: true)); // as the app does
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

    private QuestionItem Item(Question question) => _view!.Pending.Concat(_view.Answered).Single(i => i.Question == question);

    // Keyboard.FocusedElement can become null while Drain pumps desktop deactivation messages. Logical focus belongs
    // to this window and survives that loss; do not reacquire focus after the action, which could hide a wrong stop.
    private static IInputElement Focused(Window window) => Assert.IsAssignableFrom<IInputElement>(FocusManager.GetFocusedElement(window));

    private static void Focus(Window window, IInputElement element)
    {
        window.Activate();
        WpfContext.Drain(); // finish activation's queued Refocus before establishing the action's starting focus
        Assert.Same(element, Keyboard.Focus(element));
        Assert.Same(element, Focused(window));
    }

    /// <summary>What Windows does when the window comes to the front, without taking the foreground in a test run.</summary>
    private static void Activated(Window window)
    {
        typeof(Window).GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [EventArgs.Empty]);
        WpfContext.Drain();
    }

    /// <summary>
    /// Presses the button as UI Automation and the keyboard do, through its own click handling; one that does not
    /// respond fails the test instead of being skipped.
    /// </summary>
    private static void Press(Button button)
    {
        Assert.True(button.IsEnabled, $"{button.Name} does not respond");
        Invoke(button);
        WpfContext.Drain();
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

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
