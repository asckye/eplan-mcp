using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Localization;
using static EplanMcp.Workbench.Tests.Shell.TestWindows;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// The UIA tree of the window with the approval queue open (workbench-ui 15.1: A11Y-01, A11Y-02, A11Y-10, A11Y-13,
/// APR-02), walked through the automation peers as a screen reader sees it, in both languages, with a write, a
/// Destructive write and a release waiting.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Medium")]
public sealed class ApprovalsPageAutomationTests(WpfContext wpf) : IDisposable
{
    private readonly ManualTime _clock = new(Asks.Now);
    private ApprovalQueueView? _view;

    public void Dispose() => _view?.Dispose();

    [Theory]
    [InlineData(AppLanguage.Chinese)]
    [InlineData(AppLanguage.English)]
    public void EveryElementToFocusOrOperate_HasAName_ThatIsNoTypeName(AppLanguage language)
    {
        wpf.RunWithLanguage(language, () => WithQueue((window, page) =>
        {
            List<AutomationPeer> peers = Peers(window);
            var nameless = peers.Where(Operable).Select(p => (Type: p.GetType().Name, Name: p.GetName()))
                .Where(p => string.IsNullOrWhiteSpace(p.Name) || p.Name.StartsWith("System.", StringComparison.Ordinal) || p.Name.Contains("EplanMcp.", StringComparison.Ordinal) || p.Name.Contains("TiaOpenness.", StringComparison.Ordinal));

            Assert.Empty(nameless);
            Assert.DoesNotContain(peers, p => p.GetAutomationControlType() == AutomationControlType.Text && string.IsNullOrWhiteSpace(p.GetName()));
            Assert.Equal(3, peers.Count(p => p.GetAutomationControlType() == AutomationControlType.ListItem)); // the walk reached the rows
            Assert.True(peers.Count(Operable) >= 20);
        }));
    }

    [Fact]
    public void TheQueuesOwnNames_AreTheCataloguesTexts()
    {
        wpf.RunWithLanguage(AppLanguage.Chinese, () => WithQueue((window, page) =>
        {
            Loc loc = Loc.Current;
            Assert.Equal(
                ("拒绝", "批准", loc["Approvals.List"], loc["Approvals.Detail"], loc["Approvals.Groups"]),
                (Name(page, "RejectButton"), Name(page, "ApproveButton"), Name(page, "Questions"), Name(page, "DetailArea"), Name(page, "Groups")));
        }));
    }

    [Fact]
    public void EveryTextBox_IsLabelledByAVisibleLabel()
    {
        wpf.Run(() => WithQueue((window, page) =>
        {
            _view!.Select(_view.Pending.Single(q => q.IsDestructive));
            WpfContext.Drain();
            var boxes = Peers(window).Where(p => p.GetAutomationControlType() == AutomationControlType.Edit).ToList();

            Assert.Equal(2, boxes.Count); // the backup folder and the change list
            Assert.All(boxes, box =>
            {
                var label = (FrameworkElementAutomationPeer?)box.GetLabeledBy();
                Assert.NotNull(label);
                Assert.True(label.Owner.IsVisible);
                Assert.False(string.IsNullOrWhiteSpace(label.GetName()));
            });
        }));
    }

    [Fact]
    public void ThePageTitle_IsTheOnlyLevel1_TheQuestionsTitleAndItsSections_Level2()
    {
        wpf.Run(() => WithQueue((window, page) =>
        {
            Dictionary<object, AutomationHeadingLevel> headings = Peers(window).Where(p => p.GetHeadingLevel() != AutomationHeadingLevel.None)
                .ToDictionary(p => (object)((FrameworkElementAutomationPeer)p).Owner, p => p.GetHeadingLevel());

            Assert.Equal(page.FindName("PageTitle"), Assert.Single(headings, h => h.Value == AutomationHeadingLevel.Level1).Key);
            Assert.All(headings.Where(h => h.Value != AutomationHeadingLevel.Level1), h => Assert.Equal(AutomationHeadingLevel.Level2, h.Value));
            Assert.Equal(AutomationHeadingLevel.Level2, headings[page.FindName("QuestionTitle")]);
            Assert.Equal(AutomationHeadingLevel.Level2, headings[page.FindName("ChangesLabel")]);
        }));
    }

    [Fact]
    public void NoButtonInTheWindow_IsDefaultOrCancel_SoEnterAndEscAnswerNothing()
    {
        wpf.Run(() => WithQueue((window, page) =>
        {
            List<Button> buttons = [.. Peers(window).OfType<FrameworkElementAutomationPeer>().Select(p => p.Owner).OfType<Button>()];

            Assert.Contains(page.FindName("ApproveButton"), buttons);
            Assert.All(buttons, button => Assert.False(button.IsDefault || button.IsCancel, AutomationProperties.GetName(button)));
        }));
    }

    [Fact]
    public void EveryTarget_IsAtLeast24By24()
    {
        wpf.Run(() => WithQueue((window, page) =>
        {
            List<FrameworkElement> targets = [.. Peers(window).OfType<FrameworkElementAutomationPeer>().Select(p => (FrameworkElement)p.Owner).Where(e => e is ButtonBase)];
            targets.AddRange(((ListBox)page.FindName("Questions")).Items.Cast<object>().Select(i => (FrameworkElement)((ListBox)page.FindName("Questions")).ItemContainerGenerator.ContainerFromItem(i)));
            var small = targets.Where(e => e.IsVisible && (e.ActualWidth < 24 || e.ActualHeight < 24))
                .Select(e => $"{e.GetType().Name} {AutomationProperties.GetName(e)} {e.ActualWidth}x{e.ActualHeight}");

            Assert.True(targets.Count >= 14);
            Assert.Empty(small);
        }));
    }

    private void WithQueue(Action<MainWindow, ApprovalsPage> test) => With(window =>
    {
        var queue = new ApprovalQueue(_clock);
        _view = new ApprovalQueueView(queue, _clock);
        queue.Changed += q => _view.Update(q);
        window.Attach(_view);
        foreach ((ApprovalAskParams ask, int seconds) in new[] { (Asks.Write("r1"), 60), (Asks.Write("r2", tier: QuestionTier.Destructive, tool: "eplan_page_delete"), 90), (Asks.Release("r3"), 120) })
        {
            queue.Add(ask with { DeadlineUnixMs = (_clock.GetUtcNow() + TimeSpan.FromSeconds(seconds)).ToUnixTimeMilliseconds() });
        }

        WpfContext.Drain();
        _clock.Advance(ResponseGuard.Delay); // the buttons respond, as when the engineer reads the question
        WpfContext.Drain();
        test(window, (ApprovalsPage)window.FindName("ApprovalsPage"));
    });

    /// <summary>What a screen reader can reach: the control view of the peers, the visible part of the tree.</summary>
    private static List<AutomationPeer> Peers(Window window)
    {
        var found = new List<AutomationPeer>();
        void Walk(AutomationPeer peer)
        {
            found.Add(peer);
            foreach (AutomationPeer child in peer.GetChildren() ?? [])
            {
                Walk(child);
            }
        }

        Walk(UIElementAutomationPeer.CreatePeerForElement(window));
        return [.. found.Where(p => p.IsControlElement() && (p is not FrameworkElementAutomationPeer element || element.Owner.IsVisible))];
    }

    private static bool Operable(AutomationPeer peer) => peer.IsKeyboardFocusable()
        || peer.GetPattern(PatternInterface.Invoke) is not null || peer.GetPattern(PatternInterface.Toggle) is not null
        || peer.GetPattern(PatternInterface.SelectionItem) is not null;

    private static string Name(ApprovalsPage page, string element) => AutomationProperties.GetName((DependencyObject)page.FindName(element));
}
