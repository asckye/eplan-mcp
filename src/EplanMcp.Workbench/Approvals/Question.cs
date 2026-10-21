using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Approvals;

/// <summary>
/// One question from a Host (ADR-0014 decision 3) and what became of it. <see cref="End"/>, <see cref="Execution"/>
/// and <see cref="Reason"/> change only under the <see cref="ApprovalQueue"/>'s lock, which raises
/// <see cref="ApprovalQueue.Changed"/> afterwards; the question itself is the Host's, untouched (its texts are project
/// data, shown only after cleaning).
/// </summary>
internal sealed class Question
{
    internal Question(ApprovalAskParams ask, long sequence, DateTimeOffset arrived)
    {
        Ask = ask;
        Sequence = sequence;
        Arrived = arrived;
    }

    public ApprovalAskParams Ask { get; }

    public string RequestId => Ask.RequestId;

    /// <summary>The order of arrival, which breaks ties between equal deadlines.</summary>
    public long Sequence { get; }

    public DateTimeOffset Arrived { get; }

    public DateTimeOffset Deadline => DateTimeOffset.FromUnixTimeMilliseconds(Ask.DeadlineUnixMs);

    /// <summary>How the question ended; null while it waits.</summary>
    public QuestionEnd? End { get; internal set; }

    /// <summary>What became of the approved write (APR-12); <see cref="QuestionExecution.None"/> for anything else.</summary>
    public QuestionExecution Execution { get; internal set; }

    /// <summary>The error code of a write that failed or whose outcome is unknown, such as <c>PLAN_STALE</c>.</summary>
    public string? Reason { get; internal set; }

    /// <summary>
    /// The answered group let it go, the oldest of more than <see cref="ApprovalQueue.MaxAnswered"/>: it shows no more and
    /// nothing changes it.
    /// </summary>
    public bool Dropped { get; internal set; }

    /// <summary>The answer the connection sends; set once, when the question ends.</summary>
    internal TaskCompletionSource<WorkbenchDecision> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
