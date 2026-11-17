using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ApprovalAsk"/> (M3 design 4.3, 4.7): shows the Host's question in the approval dialog and
/// answers with the decision and the question's binding. It runs on EPLAN's main thread like every operation, so the
/// dispatcher refuses it while another dialog is open, and refuses other calls while it is. A deadline already passed
/// answers "expired" without showing anything.
/// </summary>
public sealed class ApprovalAskOperation(IApprovalDialog dialog, Func<DateTimeOffset> utcNow) : Operation<ApprovalAskArgs, ApprovalAskData>
{
    public const int MaxChanges = 1000;

    public const int MaxLineLength = 1000;

    /// <summary>The longest a dialog may stay open; the Host's deadline is at most five minutes (ADR-0011).</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(15);

    public ApprovalAskOperation(IApprovalDialog dialog)
        : this(dialog, () => DateTimeOffset.UtcNow)
    {
    }

    public override string Name => OperationNames.ApprovalAsk;

    protected override ApprovalAskData Execute(ApprovalAskArgs args)
    {
        TimeSpan timeout = TimeSpan.FromMilliseconds(args.DeadlineUnixMs - utcNow().ToUnixTimeMilliseconds());
        var violations = new List<string>();
        if (new[] { args.RequestId, args.Fingerprint, args.ArgumentsHash, args.Title, args.ApproveLabel, args.RejectLabel }.Any(string.IsNullOrWhiteSpace))
        {
            violations.Add("'requestId', 'fingerprint', 'argumentsHash', 'title', 'approveLabel' and 'rejectLabel' must not be empty.");
        }

        if (args.Changes is not { Count: > 0 and <= MaxChanges } changes || changes.Any(c => c is null || c.Length > MaxLineLength))
        {
            violations.Add("'changes' must have 1-1000 lines of at most 1000 characters.");
        }

        if (timeout > MaxTimeout)
        {
            violations.Add("'deadlineUnixMs' must be at most 15 minutes ahead.");
        }

        if (violations.Count > 0)
        {
            throw InvalidArgument(string.Join(" ", violations));
        }

        string decision = timeout <= TimeSpan.Zero ? ApprovalDecisions.Expired : dialog.Ask(args, timeout);
        return new ApprovalAskData
        {
            RequestId = args.RequestId,
            Fingerprint = args.Fingerprint,
            ArgumentsHash = args.ArgumentsHash,
            Decision = decision is ApprovalDecisions.Granted or ApprovalDecisions.Denied or ApprovalDecisions.Expired ? decision : ApprovalDecisions.Cancelled,
        };
    }
}
