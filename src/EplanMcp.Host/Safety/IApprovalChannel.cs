using EplanMcp.Contracts;

namespace EplanMcp.Host.Safety;

/// <summary>What the engineer decided, or why nobody decided (ADR-0005 section 3). Only <see cref="Granted"/> lets a write run.</summary>
internal enum ApprovalDecision
{
    Granted,

    /// <summary>The engineer rejected the change.</summary>
    Denied,

    /// <summary>The question was closed without a choice.</summary>
    Cancelled,

    /// <summary>Not answered before the deadline, or answered after it.</summary>
    Expired,

    /// <summary>No channel could ask the engineer.</summary>
    Unavailable,
}

/// <summary>
/// One question to the engineer: may <see cref="Tool"/> make the changes of <see cref="Plan"/> in <see cref="Plan"/>'s
/// project? <see cref="RequestId"/> is the Host's ID of this write, never one a client gave; with the plan's
/// fingerprint and <see cref="ArgumentsHash"/> it binds the answer to this call. <see cref="Changes"/>: the plan as one
/// line per change. Unanswered at <see cref="Deadline"/>, the question has expired. With <see cref="Release"/>, the
/// question is instead whether the engineer has checked the project after that write of unknown outcome, so that its
/// suspension may be lifted before this call goes on (ADR-0010 section 3); it is never merged with the approval.
/// <see cref="BackupFolder"/>: for a Destructive write, the folder its backup goes into first (ADR-0013).
/// </summary>
internal sealed record ApprovalQuestion(string RequestId, string Tool, string ArgumentsHash, WritePlan Plan, IReadOnlyList<string> Changes, DateTimeOffset Deadline, Suspension? Release = null, string? BackupFolder = null);

/// <summary>What a channel got back: the decision and the binding it was given for.</summary>
internal sealed record ApprovalAnswer(string RequestId, string Fingerprint, string ArgumentsHash, ApprovalDecision Decision);

/// <summary>A way to ask the engineer (ADR-0005 section 3). <see cref="ApprovalService"/> tries them in order.</summary>
internal interface IApprovalChannel
{
    /// <summary>The channel's name in the audit.</summary>
    public string Name { get; }

    /// <summary>Where the engineer answers, for the progress the client sees while it waits: "in EPLAN".</summary>
    public string Where { get; }

    /// <summary>
    /// Asks <paramref name="question"/> and waits for the answer, at the latest until its deadline; null when this
    /// channel cannot ask now, so the next one is tried. Throws <see cref="Bridge.BridgeCallException"/> when asking
    /// failed in a way the model must hear, for example EPLAN_BUSY; nothing has run then.
    /// </summary>
    public Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken);
}
