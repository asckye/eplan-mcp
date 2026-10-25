using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>Find sample targets from an independent snapshot; no fixed oid and no fixture mutation.</summary>
internal static class LivePropertyObjects
{
    internal static async Task<LiveSnapshots.Snapshot> SnapshotAsync(BridgeClient bridge, LiveEplan eplan) =>
        await LiveSnapshots.TakeAsync(bridge, new ProjectSnapshotArgs { Project = eplan.ProjectPath });

    // In the sample, the devices with two-language FUNC_TEXT and those with FUNC_TECHNICAL_CHARACTERISTIC are different
    // ones (26 and 63 unique mains, none with both), so a test picks a device per property.
    internal static SnapshotObject Device(LiveSnapshots.Snapshot snapshot, int property = 20011) => snapshot.Objects("function")
        .Where(o => o.Main == true && o.Name is not null)
        .GroupBy(o => o.Name, StringComparer.Ordinal)
        .Where(g => g.Count() == 1)
        .Select(g => g.Single())
        .First(o => o.Values.Any(v => v.Id == property && (property != 20011 || v.Texts?.Count >= 2)));

    internal static SnapshotObject Connection(LiveSnapshots.Snapshot snapshot) => snapshot.Objects("connection")
        .GroupBy(o => (o.Page, o.Start?.Function, o.Start?.Pin, EndFunction: o.End?.Function, EndPin: o.End?.Pin))
        .Where(g => g.Count() == 1)
        .Select(g => g.Single())
        .First(o => o.Page is not null && o.Values.Any(v => v.Id == 31002) && o.Values.Any(v => v.Id == 31004)
            && snapshot.Page(o.Page).Objects.Count(obj => obj.Kind == "connection") <= 200);

    internal static ObjectLocator Locator(LiveEplan eplan, SnapshotObject obj) => new()
    {
        Project = eplan.ProjectPath,
        Kind = obj.Kind,
        Type = obj.Type,
        Oid = obj.Oid,
        Name = obj.Name,
        Page = obj.Page,
        Category = obj.Category,
        Pos = obj.Pos,
    };

    internal static PropertyReadArgs Args(LiveEplan eplan, PropertyTarget target, params int[] ids)
    {
        PropertyReadArgs args = LiveProperties.Args(eplan, false, ids);
        args.Target = target;
        args.Language = null;
        return args;
    }
}
