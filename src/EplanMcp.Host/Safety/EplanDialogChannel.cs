using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Safety;

/// <summary>
/// Asks the engineer in a dialog in EPLAN (M3 design 4.3, 4.7, step 12): <c>approval.ask</c> with the texts of
/// <see cref="ApprovalTexts"/> in the user interface language. The Host waits for the answer, holding its queue to
/// EPLAN, until the deadline and the Bridge's dispatch timeout have passed; the dialog closes itself at the deadline.
/// An add-in without the operation cannot ask. Errors (EPLAN_BUSY while EPLAN shows another dialog) go to the caller.
/// </summary>
internal sealed class EplanDialogChannel(IBridgeClient bridge, TimeProvider clock, HttpCaller? caller = null) : IApprovalChannel
{
    public const string ChannelName = "eplan_dialog";

    /// <summary>Beyond the deadline: the Bridge's wait for EPLAN's main thread (10 s) and a margin for the answer.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(15);

    public string Name => ChannelName;

    public string Where => "in EPLAN";

    public async Task<ApprovalAnswer?> AskAsync(ApprovalQuestion question, CancellationToken cancellationToken)
    {
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Handshake is not { } handshake || !BridgeClient.Supports(handshake, OperationNames.ApprovalAsk))
        {
            return null;
        }

        ApprovalAskArgs args = ApprovalTexts.For(question, CultureInfo.CurrentUICulture);
        args.Summary = caller?.Name is { } name ? name + "\n" + args.Summary : args.Summary;
        args.RequestId = question.RequestId;
        args.Fingerprint = question.Plan.Fingerprint;
        args.ArgumentsHash = question.ArgumentsHash;
        args.DeadlineUnixMs = question.Deadline.ToUnixTimeMilliseconds();
        TimeSpan wait = question.Deadline - clock.GetUtcNow() + Slack;
        ApprovalAskData data = await bridge.InvokeAsync<ApprovalAskArgs, ApprovalAskData>(OperationNames.ApprovalAsk, args, wait, null, cancellationToken).ConfigureAwait(false);
        ApprovalDecision decision = data.Decision switch
        {
            ApprovalDecisions.Granted => ApprovalDecision.Granted,
            ApprovalDecisions.Denied => ApprovalDecision.Denied,
            ApprovalDecisions.Expired => ApprovalDecision.Expired,
            _ => ApprovalDecision.Cancelled,
        };
        return new ApprovalAnswer(data.RequestId, data.Fingerprint, data.ArgumentsHash, decision);
    }
}
