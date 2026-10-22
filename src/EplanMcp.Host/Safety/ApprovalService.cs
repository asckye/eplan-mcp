using EplanMcp.Contracts;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace EplanMcp.Host.Safety;

/// <summary>
/// An approval's outcome: the channel that asked (null when none could) and the decision; for a grant of a write, where
/// the write's outcome goes, which the caller disposes.
/// </summary>
internal sealed record ApprovalResult(string? Channel, ApprovalDecision Decision)
{
    public IApprovalOutcome? Outcome { get; init; }
}

/// <summary>
/// The approval step of the write pipeline (ADR-0005 section 3, ADR-0011, M3 design 4.3): asks the engineer about one
/// concrete, already planned change list through the first channel that can, in registration order, and refuses when
/// none can. An answer counts only for the question it was asked for: one that names another request ID, fingerprint
/// or argument hash is denied, and a grant after the deadline has expired. The wait happens here in the Host (4.7).
/// Stateless and thread-safe.
/// </summary>
internal sealed partial class ApprovalService(IEnumerable<IApprovalChannel> channels, HostConfig config, TimeProvider clock, ILogger<ApprovalService> logger)
{
    /// <summary>How long the engineer has to answer: <see cref="HostConfig.ApprovalTimeout"/> (ADR-0011).</summary>
    public TimeSpan Timeout => config.ApprovalTimeout;

    internal const string NoChannel =
        "This change needs the engineer's approval, but no approval channel can ask the engineer now, so it was not run and nothing was changed.";

    private readonly IApprovalChannel[] _channels = [.. channels];

    /// <summary>
    /// Asks about <paramref name="plan"/> of write <paramref name="requestId"/> by <paramref name="tool"/> of
    /// <paramref name="tier"/> to EPLAN <paramref name="eplan"/>, and returns the decision, reporting the wait to
    /// <paramref name="progress"/> (see <see cref="ApprovalProgress"/>). <paramref name="backupFolder"/>: where a
    /// Destructive write backs the project up first, which the dialog names; null for other writes;
    /// <paramref name="masterDataBackupFolder"/> the same for the master data a write changes (ADR-0019). Throws
    /// <see cref="Bridge.BridgeCallException"/> when a channel failed to ask; nothing has run then.
    /// </summary>
    public Task<ApprovalResult> AskAsync(string requestId, string tool, ToolTier tier, string argumentsHash, WritePlan plan, EplanInstanceInfo eplan, string? backupFolder, string? masterDataBackupFolder, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken) =>
        AskAsync(
            new ApprovalQuestion(requestId, tool, argumentsHash, plan, [.. WritePipeline.ChangeLines(plan)], clock.GetUtcNow() + Timeout, BackupFolder: backupFolder)
            {
                Tier = tier,
                Eplan = eplan,
                MasterDataBackupFolder = masterDataBackupFolder,
            },
            progress,
            cancellationToken);

    /// <summary>
    /// Asks, before write <paramref name="requestId"/> goes on, whether the engineer has checked the project after the
    /// suspended write <paramref name="suspension"/> (ADR-0010 section 3), through the same channels as an approval and
    /// as a question of its own. Only <see cref="ApprovalDecision.Granted"/> lets the suspension be lifted.
    /// </summary>
    public Task<ApprovalResult> ConfirmReleaseAsync(string requestId, string tool, ToolTier tier, string argumentsHash, WritePlan plan, EplanInstanceInfo eplan, Suspension suspension, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken) =>
        AskAsync(new ApprovalQuestion(requestId, tool, argumentsHash, plan, suspension.Changes, clock.GetUtcNow() + Timeout, suspension) { Tier = tier, Eplan = eplan }, progress, cancellationToken);

    /// <summary>
    /// Asks the engineer to confirm installing someone else's engineering pack <paramref name="install"/> (ADR-0024
    /// section 4), bound to its digest, through the same channels as an approval; the Workbench cannot ask it in protocol
    /// version 1, so EPLAN's dialog does, in EPLAN <paramref name="eplan"/>.
    /// </summary>
    public Task<ApprovalResult> ConfirmPackInstallAsync(string requestId, string tool, Packs.PackInstallInfo install, EplanInstanceInfo eplan, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken) =>
        AskAsync(
            new ApprovalQuestion(requestId, tool, install.Digest, new WritePlan { Fingerprint = install.Digest }, [], clock.GetUtcNow() + Timeout) { Tier = ToolTier.Write, Eplan = eplan, PackInstall = install },
            progress,
            cancellationToken);

    private async Task<ApprovalResult> AskAsync(ApprovalQuestion question, IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        string requestId = question.RequestId;
        string tool = question.Release is null ? question.Tool : "release of " + question.Tool;
        if (question.Eplan is { Offline: true })
        {
            // The offline host has no user interface, and writes through it go to scratch only (M5 design 4.9): no
            // channel asks about it, not even a Workbench that is running.
            LogDecided(requestId, tool, "none (offline host)", ApprovalDecision.Unavailable);
            return new ApprovalResult(null, ApprovalDecision.Unavailable);
        }

        using ApprovalProgress? waiting = ApprovalProgress.Start(progress, question, Timeout, clock);
        foreach (IApprovalChannel channel in _channels)
        {
            waiting?.AskingIn(channel.Where);
            if (await channel.AskAsync(question, cancellationToken).ConfigureAwait(false) is { } answer)
            {
                ApprovalDecision decision = Checked(question, answer, channel.Name);
                LogDecided(requestId, tool, channel.Name, decision);

                // Only a write that may now run has an outcome to show; a confirmation is followed by its own approval.
                bool shows = decision == ApprovalDecision.Granted && question.Release is null && question.PackInstall is null;
                if (!shows)
                {
                    answer.Outcome?.Dispose();
                }

                return new ApprovalResult(channel.Name, decision) { Outcome = shows ? answer.Outcome : null };
            }
        }

        LogDecided(requestId, tool, "none", ApprovalDecision.Unavailable);
        return new ApprovalResult(null, ApprovalDecision.Unavailable);
    }

    /// <summary>The error for a decision other than <see cref="ApprovalDecision.Granted"/>: nothing was run.</summary>
    public BridgeError Refusal(ApprovalDecision decision) => decision switch
    {
        ApprovalDecision.Denied => new() { Reason = ErrorReason.ApprovalDenied, Message = "The engineer rejected this change, so it was not run and nothing was changed. Do not call again unless the engineer asks for it." },
        ApprovalDecision.Cancelled => new() { Reason = ErrorReason.ApprovalDenied, Message = "The engineer closed the approval without deciding, so the change was not run and nothing was changed. Ask the engineer before calling again." },
        ApprovalDecision.Expired => new()
        {
            Reason = ErrorReason.ApprovalExpired,
            Message = $"The engineer did not approve within {HostConfig.Seconds(Timeout)} seconds, so the change was not run and nothing was changed. Call again to ask again.",
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
