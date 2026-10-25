using System.Collections.Concurrent;

namespace EplanMcp.Host.Plans;

/// <summary>Owned by one Host runtime. Concurrent callers share immutable compile digests; nothing is persisted.</summary>
internal sealed class PlanTrust
{
    private sealed record Entry(PlanOwner Owner, string Hash);
    private readonly ConcurrentDictionary<string, Entry> _compiled = new(StringComparer.Ordinal);

    public bool Owns(string planId, PlanOwner owner) =>
        _compiled.TryGetValue(planId, out Entry? trusted) && trusted.Owner == owner;

    public string Register(PlanDefinition definition)
    {
        string hash = PlanCanonical.Hash(definition);
        if (!_compiled.TryAdd(definition.PlanId, new(definition.Owner, hash)))
        {
            throw new InvalidOperationException("A compiled plan ID cannot be registered again.");
        }

        return hash;
    }

    public bool Matches(PlanDefinition definition, PlanOwner owner, string recordedHash) =>
        _compiled.TryGetValue(definition.PlanId, out Entry? trusted) && trusted.Owner == owner && trusted.Owner == definition.Owner
        && string.Equals(trusted.Hash, recordedHash, StringComparison.Ordinal)
        && string.Equals(trusted.Hash, PlanCanonical.Hash(definition), StringComparison.Ordinal);
}
