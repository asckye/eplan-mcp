using System.Collections.ObjectModel;
using System.ComponentModel;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The approval queue page without its window (workbench-ui section 11, M5 design 4.6): the waiting questions, earliest
/// deadline first; the ended ones, latest first, as many as the queue keeps; the question shown, kept as that very
/// question so an arrival never replaces it (APR-05), not by request ID, which a write's releases and approval share;
/// the 500 ms guard (APR-04); and the one way to answer, the shown question's two buttons (APR-01, APR-06).
/// Used on the UI thread: queue changes come in through <see cref="Update"/>.
/// </summary>
internal sealed class ApprovalQueueView : INotifyPropertyChanged, IDisposable
{
    private readonly ApprovalQueue _queue;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Question, QuestionItem> _items = [];
    private readonly Dictionary<QuestionItem, QuestionExecution> _executions = [];
    private TimeSpan? _left;

    public ApprovalQueueView(ApprovalQueue queue, TimeProvider clock)
    {
        _queue = queue;
        _clock = clock;
        Guard = new ResponseGuard(clock);
        Guard.Changed += () => OnChanged(nameof(CanAnswer));
        foreach (Question question in queue.Answered.Concat(queue.Pending))
        {
            Update(question);
        }

        Loc.Current.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A different question is shown: the page moves focus to its title when the engineer caused it (APR-03).</summary>
    public event Action? ShownChanged;

    /// <summary>Something for the live region (STS-02, A11Y-08): an arrival, 30 or 10 seconds left, an end, an outcome.</summary>
    public event Action<string>? Announced;

    /// <summary>A question arrived: the reminders that do not take focus follow (APR-14).</summary>
    public event Action<QuestionItem>? Arrived;

    public ObservableCollection<QuestionItem> Pending { get; } = [];

    public ObservableCollection<QuestionItem> Answered { get; } = [];

    /// <summary>The question in the detail: waiting, or ended with its reason and without buttons (APR-11).</summary>
    public QuestionItem? Shown { get; private set; }

    public ResponseGuard Guard { get; }

    /// <summary>Whether the two buttons respond: a waiting question is shown and the guard is open (APR-04).</summary>
    public bool CanAnswer => Shown is { IsPending: true } && Guard.IsOpen;

    /// <summary>Nothing waits and nothing is shown: the empty state (ERR-05).</summary>
    public bool IsEmpty => Pending.Count == 0 && Shown is null;

    /// <summary>Takes in a change of the queue, after its <see cref="ApprovalQueue.Changed"/>.</summary>
    public void Update(Question question)
    {
        if (question.Dropped)
        {
            Forget(question);
            return;
        }

        if (!_items.TryGetValue(question, out QuestionItem? item))
        {
            item = new QuestionItem(question, _clock);
            _items.Add(question, item);
        }

        if (question.End is null && !Pending.Contains(item))
        {
            bool nothingWaited = Pending.Count == 0;
            int at = 0;
            while (at < Pending.Count && (Pending[at].Question.Deadline, Pending[at].Question.Sequence).CompareTo((question.Deadline, question.Sequence)) < 0)
            {
                at++;
            }

            Pending.Insert(at, item);
            Announced?.Invoke(Loc.Current.T("Question.Arrived", item.Tool));
            Arrived?.Invoke(item);
            if (nothingWaited)
            {
                Show(item); // APR-05: only into an empty queue, and never moving focus (APR-14)
            }
        }
        else if (question.End is not null)
        {
            bool ending = Pending.Remove(item);
            if (!Answered.Contains(item))
            {
                Answered.Insert(0, item);
            }

            if (ending && item == Shown)
            {
                Announced?.Invoke(item.Result); // the detail keeps it, with its reason and without buttons
            }
            else if (_executions.TryGetValue(item, out QuestionExecution before) && before != question.Execution && question.Execution != QuestionExecution.Running)
            {
                Announced?.Invoke(item.Outcome);
            }

            _executions[item] = question.Execution;
        }

        item.Refresh();
        OnChanged(nameof(CanAnswer), nameof(IsEmpty));
    }

    /// <summary>The engineer chose a question from a list: it shows, and the buttons wait (APR-04, APR-06).</summary>
    public void Select(QuestionItem item) => Show(item);

    /// <summary>
    /// The only way to answer: the question shown, while it waits, once the guard is open. False when that is not so,
    /// or the question ended at that very moment. The next waiting question then shows (APR-03).
    /// </summary>
    public bool Answer(bool approve)
    {
        if (!CanAnswer || Shown is not { } answered || !_queue.Answer(answered.Question, approve))
        {
            return false;
        }

        Show(Pending.FirstOrDefault(i => i != answered && i.IsPending));
        return true;
    }

    /// <summary>Once a second: the countdowns move, and the shown question's 30 and 10 seconds are told (APR-10).</summary>
    public void Tick()
    {
        foreach (QuestionItem item in Pending)
        {
            item.Refresh();
        }

        if (Shown is { IsPending: true } shown)
        {
            if (_left is { } before && Countdown.Crossed(before, shown.Left) is { } seconds)
            {
                Announced?.Invoke(Loc.Current.T("Question.SecondsLeft", seconds));
            }

            _left = shown.Left;
        }
    }

    public void Dispose()
    {
        Loc.Current.LanguageChanged -= OnLanguageChanged;
        Guard.Dispose();
    }

    /// <summary>
    /// The queue let an old answered question go: so does the page, and with it the question and its texts, unless the
    /// engineer is looking at it right now.
    /// </summary>
    private void Forget(Question question)
    {
        if (_items.Remove(question, out QuestionItem? item))
        {
            Answered.Remove(item);
            _executions.Remove(item);
            OnChanged(nameof(IsEmpty));
        }
    }

    private void Show(QuestionItem? item)
    {
        if (item == Shown)
        {
            return;
        }

        Shown = item;
        _left = item?.Left;
        Guard.Arm();
        OnChanged(nameof(Shown), nameof(CanAnswer), nameof(IsEmpty));
        ShownChanged?.Invoke();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (QuestionItem item in _items.Values)
        {
            item.Refresh();
        }
    }

    private void OnChanged(params string[] names)
    {
        foreach (string name in names)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
