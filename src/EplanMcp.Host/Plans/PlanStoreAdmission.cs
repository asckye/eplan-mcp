namespace EplanMcp.Host.Plans;

internal sealed partial class PlanStore
{
    /// <summary>Published executable plans already reserve 16 MiB (P04); recheck it before taking the project lock.</summary>
    public async Task CheckAdmissionAsync(string planId, CancellationToken cancellationToken)
    {
        using FileStream held = await LockAsync(".store.lock", cancellationToken).ConfigureAwait(false);
        long reserved = RecordFiles().Where(f => Path.GetFileNameWithoutExtension(f) != planId && PlanCapacity.CanGrow(ReadKnown(f), Clock.GetUtcNow()))
            .Sum(f => Math.Max(0, PlanCapacity.PerPlan - PlanBytes(Path.GetFileNameWithoutExtension(f))));
        PlanCapacity.Require("plans.bytes", Usage().Bytes + reserved, Math.Max(0, PlanCapacity.PerPlan - PlanBytes(planId)), PlanCapacity.Total);
    }
}
