using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using ModelContextProtocol;

namespace EplanMcp.Host.Safety;

internal sealed partial class ApprovalService
{
    /// <summary>One plan question with fresh binding; no v1 outcome can describe a plan's five execution states.</summary>
    public async Task<ApprovalResult> AskPlanAsync(PlanStore store, PlanRecord record, string backupFolder,
        IReadOnlyList<string> independentFingerprints, EplanInstanceInfo eplan,
        IProgress<ProgressNotificationValue>? progress, CancellationToken cancellationToken)
    {
        await CheckChangesAsync(store, record, cancellationToken).ConfigureAwait(false);
        var view = new PlanApprovalView(record, store.ChangesPath(record.Definition.PlanId), backupFolder);
        DateTimeOffset deadline = clock.GetUtcNow() + Timeout;
        if (record.Definition.ExpiresAt < deadline)
        {
            deadline = record.Definition.ExpiresAt;
        }

        // Validate the required review text even if the first available channel will be the EPLAN dialog.
        _ = view.WorkbenchChanges(System.Globalization.CultureInfo.CurrentUICulture);
        ApprovalResult result = await AskAsync(view.Question(independentFingerprints, eplan, deadline), progress, cancellationToken).ConfigureAwait(false);
        if (result.Decision == ApprovalDecision.Granted)
        {
            await CheckChangesAsync(store, record, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task CheckChangesAsync(PlanStore store, PlanRecord record, CancellationToken cancellationToken)
    {
        try
        {
            string text = await store.ReadChangesAsync(record.Definition.PlanId, cancellationToken).ConfigureAwait(false);
            if (clock.GetUtcNow() >= record.Definition.ExpiresAt
                || PlanStore.Hash(Encoding.UTF8.GetBytes(text)) != record.Definition.Digests.ChangesDigest
                || text != PlanDiffPreview.For(record.Definition.Steps))
            {
                throw new JsonException("The approved plan text or lifetime has changed.");
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new BridgeCallException(new BridgeError
            {
                Reason = ErrorReason.PlanStale,
                Message = "The plan's complete changes file or lifetime is no longer valid. Compile a new plan before asking again.",
                Execution = ExecutionState.NotStarted,
            });
        }
    }
}
