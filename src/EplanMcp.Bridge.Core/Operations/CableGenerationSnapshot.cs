using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One complete read of the declared scope; State covers connections and page object identities.</summary>
public sealed class CableGenerationSnapshot(IReadOnlyList<CableGenerationCable> cables, int connections, string state)
{
    public IReadOnlyList<CableGenerationCable> Cables { get; } = cables;

    public string State { get; } = state;

    public CableGenerateCounts Counts() => new()
    {
        Cables = Cables.Count,
        Automatic = Cables.Count(c => c.Automatic == true),
        AutomaticUnknown = Cables.Count(c => c.Automatic is null),
        Connections = connections,
    };
}
