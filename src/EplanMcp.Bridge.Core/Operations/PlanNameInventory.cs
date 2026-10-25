using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Persistent metadata only, shared by names and closure readers; no API objects survive the call.</summary>
public sealed class PlanNameInventory
{
    private readonly HashSet<(int Type, long Oid)> _identities = [];

    public string? ProjectGuid { get; set; }
    public List<string> Pages { get; } = [];
    public List<PlanFunction> Functions { get; } = [];

    /// <summary>Merge finder and placement metadata by persistent identity, keeping repeated names and mains.</summary>
    public void AddFunction(PlanFunction function)
    {
        if (_identities.Add((function.Type, function.Oid)))
        {
            Functions.Add(function);
        }
    }
}
