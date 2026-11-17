using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Tries recorded-page objects before catalog-wide discovery, with the same kind and digest checks.</summary>
internal static class ApiTryTargets
{
    internal static T Resolve<T>(DrawingLocator locator, string[] kinds, IEnumerable<T> nearby,
        Func<string, IEnumerable<T>> candidates, Func<T, string, (SnapshotObject Identity, string Page)?> describe) where T : class
    {
        foreach (T obj in nearby)
        {
            foreach (string kind in kinds)
            {
                if (Matches(obj, kind))
                { return obj; }
            }
        }
        foreach (string kind in kinds)
        {
            foreach (T obj in candidates(kind))
            {
                if (Matches(obj, kind))
                { return obj; }
            }
        }
        throw new BridgeErrorException(ErrorReason.PlanStale, "Object identity changed; get a fresh locator from eplan_object_find.");

        bool Matches(T obj, string kind) => describe(obj, kind) is { } found && found.Identity.Type == locator.Type
            && found.Identity.Oid == locator.Oid && found.Page == locator.Page
            && SnapshotCanonical.Sha256(JsonConvert.SerializeObject(found.Identity)) == locator.Digest;
    }
}
