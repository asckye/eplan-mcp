using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Property-free target selection; never replaces a stale locator with a same-name object (A32/A34).</summary>
public static class PropertyReadTargets
{
    public static T Main<T>(string projectPath, PropertyTarget target, IEnumerable<T> objects, Func<T, SnapshotObject> describe)
    {
        var matches = objects.Select(o => (Object: o, Identity: describe(o)))
            .Where(o => o.Identity.Main == true && string.Equals(o.Identity.Name, target.DeviceTag, StringComparison.Ordinal)
                && (target.Page is null || string.Equals(o.Identity.Page, target.Page, StringComparison.Ordinal)))
            .GroupBy(o => (o.Identity.Type, o.Identity.Oid)).Select(g => g.First())
            .OrderBy(o => o.Identity.Page, StringComparer.Ordinal).ThenBy(o => o.Identity.Type).ThenBy(o => o.Identity.Oid).ToArray();
        if (matches.Length == 0)
        {
            throw new BridgeErrorException(ErrorReason.DeviceNotFound, "No main function has this exact full device tag on the selected page. Check the tag and page.");
        }

        if (matches.Length > 1)
        {
            ObjectLocator[] candidates = [.. matches.Take(20).Select(o => new ObjectLocator
            {
                Project = projectPath,
                Kind = "function",
                Type = o.Identity.Type,
                Oid = o.Identity.Oid,
                Name = PropertyReadText.CleanText(o.Identity.Name, out _),
                Page = PropertyReadText.CleanText(o.Identity.Page, out _),
                Category = o.Identity.Category,
                Pos = o.Identity.Pos,
            })];
            throw new BridgeErrorException(ErrorReason.AmbiguousName, "Several main functions have this full device tag. Select a candidate locator or qualify the page.",
                metadata: new Dictionary<string, string> { ["candidates"] = BridgeJson.FromObject(candidates).ToString(Formatting.None) });
        }

        return matches[0].Object;
    }

    public static T Located<T>(string projectPath, ObjectLocator locator, IEnumerable<T> objects, Func<T, SnapshotObject> describe)
        where T : class
    {
        T? found = null;
        SnapshotObject? identity = null;
        if (ProjectResolver.SamePath(projectPath, locator.Project))
        {
            foreach (T obj in objects)
            {
                SnapshotObject candidate = describe(obj);
                if (candidate.Kind == locator.Kind && candidate.Oid == locator.Oid)
                {
                    candidate.Name = candidate.Name is null ? null : SnapshotValues.Text(candidate.Name);
                    candidate.Page = candidate.Page is null ? null : SnapshotValues.Text(candidate.Page);
                    (found, identity) = (obj, candidate);
                    if (candidate.Type == locator.Type)
                    {
                        break;
                    }
                }
            }
        }

        LocatorResult check = LocatorCheck.Check(0, locator, projectPath, identity);
        if (check.Status != LocatorResult.Resolved)
        {
            throw new BridgeErrorException(ErrorReason.PlanStale, "The locator no longer matches its object. Read a fresh locator and retry.",
                metadata: new Dictionary<string, string> { ["check"] = check.Reason! });
        }

        return found!;
    }
}
