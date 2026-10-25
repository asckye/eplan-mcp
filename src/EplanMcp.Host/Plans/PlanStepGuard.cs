using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>Typed read call only. The caller supplies freshly rehashed resources and binding state (design 4.4).</summary>
internal sealed class PlanStepGuard(IBridgeClient bridge)
{
    public async Task<string> CheckAsync(string project, PlanStep step, IEnumerable<string> actualResourceHashes,
        PlanBinding? actualBinding, CancellationToken cancellationToken) =>
        await bridge.InvokeAsync<PlanStepGuardArgs, string>(OperationNames.PlanStepGuard, new()
        {
            Project = project,
            ReadScope = JsonSerializer.Deserialize<PlanGuardScope>(JsonSerializer.Serialize(step.ReadScope, PlanJson.Options), PlanJson.Options)!,
            Preconditions = JsonSerializer.Deserialize<List<PlanGuardCondition>>(JsonSerializer.Serialize(step.Preconditions, PlanJson.Options), PlanJson.Options)!,
            ResourceHashes = [.. actualResourceHashes],
            BindingRevision = actualBinding?.Revision,
            LockDigest = actualBinding?.LockDigest,
        }, TimeSpan.FromSeconds(25), null, cancellationToken).ConfigureAwait(false);
}
