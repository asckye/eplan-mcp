using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Plans;

/// <summary>Typed, fail-closed name read for LP-3; the current compiler still uses its existing baseline reader.</summary>
internal sealed class PlanNamesReader(IBridgeClient bridge)
{
    public async Task<PlanNamesData> ReadAsync(string project, CancellationToken cancellationToken)
    {
        BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Problem is { } problem)
        {
            throw new BridgeCallException(problem);
        }

        if (status.Handshake is not { } handshake || !BridgeClient.Supports(handshake, OperationNames.PlanNames))
        {
            throw Missing("plan.names");
        }

        PlanNamesData data = await bridge.InvokeAsync<PlanNamesArgs, PlanNamesData>(OperationNames.PlanNames,
            new() { Project = project }, cancellationToken).ConfigureAwait(false);
        if (!data.Complete || !Path.IsPathFullyQualified(data.ProjectPath)
            || data.NamesHash != PlanSnapshotHashes.Names(data.ProjectPath, data.Names.Select(n => new PlanOccupiedName(n.Kind, n.Name))))
        {
            throw Missing("plan.names:complete");
        }

        return data;
    }

    private static BridgeCallException Missing(string capability) => new(new BridgeError
    {
        Reason = ErrorReason.CapabilityMissing,
        Message = "Complete project names are unavailable. Use a compatible Bridge and a project within the name limits.",
        Metadata = new() { ["missing"] = capability },
    });
}
