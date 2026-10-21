using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// The questions of every connection (workbench-ui APR-13): the waiting ones and, for this run only, the ones that ended
/// (APR-11). Owner of each question's state, behind one lock: the pipe connections add questions and end them (deadline,
/// cancel, disconnect, quit), the engineer answers them, and the first of these wins. <see cref="Changed"/> follows each
/// change, outside the lock, on the thread that made it. The Host still judges whether an answer counts (SEC-06).
/// </summary>
internal sealed class ApprovalQueue(TimeProvider clock, int capacity = ApprovalQueue.DefaultCapacity)
{
    /// <summary>At this many waiting questions the Workbench takes no new one (ADR-0014 decision 3).</summary>
    public const int DefaultCapacity = 50;

    /// <summary>How many ended questions the answered group keeps; older ones drop off (the audit is the record).</summary>
    public const int MaxAnswered = 200;

    private readonly Lock _lock = new();
    private readonly List<Question> _pending = [];
    private readonly List<Question> _answered = [];
    private int _reserved;
    private long _sequence;

    /// <summary>A question arrived or changed.</summary>
    public event Action<Question>? Changed;

    /// <summary><see cref="IsAccepting"/> changed: a place was reserved or given back, or a question took or freed one.</summary>
    public event Action? AcceptingChanged;

    /// <summary>
    /// Whether a new Host's question is taken now (STS-11): some place is neither waiting nor reserved by a handshake, so
    /// the Header never says questions are taken while Hosts are turned away.
    /// </summary>
    public bool IsAccepting
    {
        get
        {
            lock (_lock)
            {
                return Free;
            }
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>The waiting questions, in order of arrival.</summary>
    public IReadOnlyList<Question> Pending => Snapshot(_pending);

    /// <summary>The ended questions of this run, oldest first.</summary>
    public IReadOnlyList<Question> Answered => Snapshot(_answered);

    /// <summary>
    /// Keeps a place for the question a handshake promised to take, so that at most <c>capacity</c> wait however many
    /// Hosts connect at once; false when the queue is full.
    /// </summary>
    public bool TryReserve()
    {
        bool full;
        lock (_lock)
        {
            if (!Free)
            {
                return false;
            }

            _reserved++;
            full = !Free;
        }

        if (full)
        {
            AcceptingChanged?.Invoke();
        }

        return true;
    }

    /// <summary>Gives back a place no question took.</summary>
    public void Release()
    {
        bool freed;
        lock (_lock)
        {
            bool before = Free;
            _reserved = Math.Max(0, _reserved - 1);
            freed = Free != before;
        }

        if (freed)
        {
            AcceptingChanged?.Invoke();
        }
    }

    /// <summary>
    /// Takes the question into a reserved place. Each question is its own, whatever its request ID: the Host asks a
    /// write's releases (ADR-0010 section 3) and then its approval all under the write's ID, each on a connection of its
    /// own. Null only while the very same question waits (same request ID, kind and suspended write), as from a Host
    /// that asks twice.
    /// </summary>
    public Question? Add(ApprovalAskParams ask)
    {
        Question? question = null;
        bool changed;
        lock (_lock)
        {
            bool before = Free;
            _reserved = Math.Max(0, _reserved - 1);
            if (!_pending.Any(q => q.RequestId == ask.RequestId && q.Ask.Kind == ask.Kind && Equals(q.Ask.Suspension, ask.Suspension)))
            {
                question = new Question(ask, ++_sequence, clock.GetUtcNow());
                _pending.Add(question);
            }

            changed = Free != before;
        }

        if (question is not null)
        {
            Changed?.Invoke(question);
        }

        if (changed)
        {
            AcceptingChanged?.Invoke();
        }

        return question;
    }

    /// <summary>The engineer's answer; false when the question no longer waits (it expired, or was cancelled first).</summary>
    public bool Answer(Question question, bool approve) => Finish(question, approve ? QuestionEnd.Approved : QuestionEnd.Rejected);

    /// <summary>Ends a waiting question without the engineer: deadline, cancel, broken connection or quitting.</summary>
    public bool End(Question question, QuestionEnd end) =>
        end is not (QuestionEnd.Approved or QuestionEnd.Rejected) && Finish(question, end);

    /// <summary>Shows what became of an approved write (APR-12); anything else is left as it is.</summary>
    public void Report(Question question, WriteExecution execution, string? reason) => Update(question, execution switch
    {
        WriteExecution.Committed => QuestionExecution.Committed,
        WriteExecution.RolledBack => QuestionExecution.RolledBack,
        WriteExecution.NotStarted => QuestionExecution.NotStarted,
        _ => QuestionExecution.Unknown,
    }, reason);

    /// <summary>The approved write's connection closed without an outcome: "no result received", nothing inferred.</summary>
    public void NoOutcome(Question question) => Update(question, QuestionExecution.NotReceived, null);

    private bool Finish(Question question, QuestionEnd end)
    {
        bool freed;
        lock (_lock)
        {
            bool before = Free;
            if (question.End is not null || !_pending.Remove(question))
            {
                return false;
            }

            freed = Free != before;

            question.End = end;
            bool write = end == QuestionEnd.Approved && question.Ask.Kind == QuestionKind.Approval;
            question.Execution = write ? QuestionExecution.Running : QuestionExecution.None;
            _answered.Add(question);
            if (_answered.Count > MaxAnswered)
            {
                _answered.RemoveAt(0);
            }
        }

        question.Decision.TrySetResult(end switch
        {
            QuestionEnd.Approved => WorkbenchDecision.Approved,
            QuestionEnd.Rejected => WorkbenchDecision.Rejected,
            QuestionEnd.Expired => WorkbenchDecision.Expired,
            _ => WorkbenchDecision.Closed,
        });
        Changed?.Invoke(question);
        if (freed)
        {
            AcceptingChanged?.Invoke();
        }

        return true;
    }

    private void Update(Question question, QuestionExecution execution, string? reason)
    {
        lock (_lock)
        {
            if (question.Execution != QuestionExecution.Running)
            {
                return;
            }

            question.Execution = execution;
            question.Reason = reason;
        }

        Changed?.Invoke(question);
    }

    /// <summary>Some place is neither waiting nor reserved; under the lock.</summary>
    private bool Free => _pending.Count + _reserved < capacity;

    private Question[] Snapshot(List<Question> questions)
    {
        lock (_lock)
        {
            return [.. questions];
        }
    }
}
