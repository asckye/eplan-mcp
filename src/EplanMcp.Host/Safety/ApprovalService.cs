using System.Globalization;
using EplanMcp.Contracts;
using Microsoft.Extensions.Logging;

namespace EplanMcp.Host.Safety;

/// <summary>An approval's outcome: the channel that asked (null when none could) and the decision.</summary>
internal sealed record ApprovalResult(string? Channel, ApprovalDecision Decision);

/// <summary>
/// The approval step of the write pipeline (ADR-0005 section 3, M3 design 4.3): asks the engineer about one concrete,
/// already planned change list through the first channel that can, in registration order, and refuses when none can.
/// An answer counts only for the question it was asked for: one that names another request ID, fingerprint or
/// argument hash is denied, and a grant after the deadline has expired. The wait happens here in the Host (4.7).
/// Stateless and thread-safe.
/// </summary>
internal sealed partial class ApprovalService(IEnumerable<IApprovalChannel> channels, TimeProvider clock, ILogger<ApprovalService> logger)
{
    /// <summary>How long the engineer has to answer: as long as a plan is kept (M3 design 4.3).</summary>
    public static readonly TimeSpan Timeout = PendingPlanStore.Lifetime;

    internal const string NoChannel =
        "This change needs the engineer's approval, but no approval channel can ask the engineer now, so it was not run and nothing was changed.";

    private readonly IApprovalChannel[] _channels = [.. channels];

    /// <summary>
    /// Asks about <paramref name="plan"/> of write <paramref name="requestId"/> and returns the decision. Throws
    /// <see cref="Bridge.BridgeCallException"/> when a channel failed to ask; nothing has run then.
    /// </summary>
    public async Task<ApprovalResult> AskAsync(string requestId, string tool, string argumentsHash, WritePlan plan, CancellationToken cancellationToken)
    {
        var question = new ApprovalQuestion(requestId, tool, argumentsHash, plan, [.. WritePipeline.ChangeLines(plan)], clock.GetUtcNow() + Timeout);
        foreach (IApprovalChannel channel in _channels)
        {
            if (await channel.AskAsync(question, cancellationToken).ConfigureAwait(false) is { } answer)
            {
                ApprovalDecision decision = Checked(question, answer, channel.Name);
                LogDecided(requestId, tool, channel.Name, decision);
                return new ApprovalResult(channel.Name, decision);
            }
        }

        LogDecided(requestId, tool, "none", ApprovalDecision.Unavailable);
        return new ApprovalResult(null, ApprovalDecision.Unavailable);
    }

    /// <summary>The error for a decision other than <see cref="ApprovalDecision.Granted"/>: nothing was run.</summary>
    public static BridgeError Refusal(ApprovalDecision decision) => decision switch
    {
        ApprovalDecision.Denied => new() { Reason = ErrorReason.ApprovalDenied, Message = "The engineer rejected this change, so it was not run and nothing was changed. Do not call again unless the engineer asks for it." },
        ApprovalDecision.Cancelled => new() { Reason = ErrorReason.ApprovalDenied, Message = "The engineer closed the approval without deciding, so the change was not run and nothing was changed. Ask the engineer before calling again." },
        ApprovalDecision.Expired => new()
        {
            Reason = ErrorReason.ApprovalExpired,
            Message = $"The engineer did not approve within {Timeout.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)} minutes, so the change was not run and nothing was changed. Call again to ask again.",
        },
        _ => new() { Reason = ErrorReason.ApprovalDenied, Message = NoChannel },
    };

    /// <summary>A mismatched answer is denied; a grant that arrives after the deadline has expired.</summary>
    private ApprovalDecision Checked(ApprovalQuestion question, ApprovalAnswer answer, string channel)
    {
        if (answer.RequestId != question.RequestId || answer.Fingerprint != question.Plan.Fingerprint || answer.ArgumentsHash != question.ArgumentsHash)
        {
            LogMismatch(question.RequestId, channel);
            return ApprovalDecision.Denied;
        }

        return answer.Decision == ApprovalDecision.Granted && clock.GetUtcNow() > question.Deadline ? ApprovalDecision.Expired : answer.Decision;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Approval of {Tool} ({RequestId}) through {Channel}: {Decision}")]
    private partial void LogDecided(string requestId, string tool, string channel, ApprovalDecision decision);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approval answer for {RequestId} from {Channel} was given for another question; denied")]
    private partial void LogMismatch(string requestId, string channel);
}
