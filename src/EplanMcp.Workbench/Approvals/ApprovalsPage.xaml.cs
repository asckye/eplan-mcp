using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EplanMcp.Workbench.Shell;
using TiaOpenness.Gui.Controls;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The approval queue page over its <see cref="ApprovalQueueView"/>. It decides where focus goes (the question's title,
/// never a button: APR-03, APR-06) and when the 500 ms guard starts counting (after rendering: APR-04), and it never
/// takes focus for an arrival (APR-14).
/// </summary>
public partial class ApprovalsPage : UserControl, IWorkbenchPage
{
    private readonly DispatcherTimer _ticks;
    private ApprovalQueueView? _view;
    private bool _focusOnShow;

    public ApprovalsPage()
    {
        InitializeComponent();
        _ticks = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => _view?.Tick(), Dispatcher);
        Unloaded += (_, _) => _ticks.Stop();
        Loaded += (_, _) => _ticks.Start();
        Show();
    }

    /// <summary>Where focus goes when this page opens: the shown question's title, else the page title (NAV-07).</summary>
    public FrameworkElement? Heading => _view?.Shown is not null ? QuestionTitle : PageTitle;

    internal ApprovalQueueView? View
    {
        get => _view;
        set
        {
            if (_view is not null)
            {
                _view.PropertyChanged -= OnViewChanged;
                _view.ShownChanged -= OnShownChanged;
                _view.Announced -= OnAnnounced;
            }

            _view = value;
            DataContext = value;
            if (value is not null)
            {
                value.PropertyChanged += OnViewChanged;
                value.ShownChanged += OnShownChanged;
                value.Announced += OnAnnounced;
            }

            Show();
        }
    }

    public void FocusHeading()
    {
        UpdateLayout();
        if (Heading is { } heading)
        {
            Keyboard.Focus(heading);
        }
    }

    /// <summary>The page or the window just became visible: the buttons wait again, from the next rendering (APR-04).</summary>
    internal void Rearm()
    {
        _view?.Guard.Arm();
        Rendered();
    }

    private void Show()
    {
        bool shown = _view?.Shown is not null;
        DetailArea.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = _view?.IsEmpty != false ? Visibility.Visible : Visibility.Collapsed;
        Questions.SelectedItem = _view?.Shown;
        ApplyButtons();

        // APR-11: the shown question ended, its buttons go; focus that was on them stays on the question, not lost.
        if (_view?.Shown is { IsPending: false } && (RejectButton.IsKeyboardFocused || ApproveButton.IsKeyboardFocused))
        {
            Keyboard.Focus(QuestionTitle);
        }
    }

    private void ApplyButtons() => RejectButton.IsEnabled = ApproveButton.IsEnabled = _view?.CanAnswer == true;

    private void OnViewChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.PropertyName == nameof(ApprovalQueueView.CanAnswer))
        {
            ApplyButtons();
        }
        else
        {
            Show();
        }
    });

    /// <summary>
    /// A different question is shown: the guard counts from when it has rendered. Focus moves only after an answer, to
    /// the next question's title; an arrival never moves it (APR-14), nor does choosing in the list.
    /// </summary>
    private void OnShownChanged()
    {
        Show();
        Rendered();
        if (_focusOnShow)
        {
            _focusOnShow = false;
            Dispatcher.BeginInvoke(FocusHeading, DispatcherPriority.Input);
        }
    }

    private void Rendered() => Dispatcher.BeginInvoke(() => _view?.Guard.Rendered(), DispatcherPriority.ContextIdle);

    private void OnAnnounced(string text)
    {
        Announcer.Text = text;
        AccessibleStatus.Announce(Announcer);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is not null && Questions.SelectedItem is QuestionItem item && item != _view.Shown)
        {
            _view.Select(item);
        }
    }

    /// <summary>Enter on a row only opens it: focus to the title, never an answer (APR-06).</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            FocusHeading();
        }
    }

    private void OnListOpen(object sender, MouseButtonEventArgs e) => FocusHeading();

    private void OnReject(object sender, RoutedEventArgs e) => Answer(approve: false);

    private void OnApprove(object sender, RoutedEventArgs e) => Answer(approve: true);

    /// <summary>The engineer answered: the next question comes up with focus on its title, not on a button (APR-03).</summary>
    private void Answer(bool approve)
    {
        _focusOnShow = true;
        if (_view?.Answer(approve) != true)
        {
            _focusOnShow = false;
        }
    }
}
