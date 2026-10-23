using System.Collections.Immutable;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Bridge;

namespace EplanMcp.Host.Tests.Plans;

internal static class PlanScopeTestData
{
    public static SnapshotObject Function(long oid, string page, string name = "+A1-K1", bool main = false) =>
        new() { Kind = "function", Type = 17, Oid = oid, Name = name, Page = page, Category = main ? "Coil" : "NoContact", Main = main, Pos = "10,20", Hash = "function-" + oid };

    public static PlanBaseline Baseline(IEnumerable<SnapshotObject>? functions = null, string[]? pages = null, string? project = null)
    {
        project ??= PlanFunctionsBridge.Project;
        SnapshotObject[] objects = [.. functions ?? [Function(11, "+A1/1", main: true), Function(12, "+A1/2"), Function(13, "+A10/1", "+A10-K2", true)]];
        int oid = 0;
        SnapshotPage[] all = [.. (pages ?? ["+A1/1", "+A1/2", "+A10/1"]).Select(name =>
        {
            var self = new SnapshotObject { Kind = "page", Type = 4, Oid = ++oid, Name = name, PageType = "Circuit", Hash = "page-" + name };
            List<SnapshotObject> contents = [self, .. objects.Where(o => o.Page == name)];
            return new SnapshotPage { Type = self.Type, Oid = self.Oid, Name = name, Objects = contents, Hash = SnapshotCanonical.PageHash("standard", contents) };
        })];
        var members = all.SelectMany(p => p.Objects).ToImmutableDictionary(o => PlanSnapshotHashes.Key(o.Kind, o.Type, o.Oid), o => o.Hash, StringComparer.Ordinal);
        string hash = PlanSnapshotHashes.Compose(all);
        var snapshot = new PlanSnapshot(new("Line1", PlanSnapshotHashes.ProjectKey(project), "sample@1"), "standard", SnapshotProfiles.Format, [.. all],
            hash, hash, all.ToImmutableDictionary(p => PlanSnapshotHashes.Key("page", p.Type, p.Oid), p => p.Hash, StringComparer.Ordinal),
            members, PlanSnapshotHashes.Members(project, members), 0);
        ImmutableArray<PlanOccupiedName> names = [.. all.SelectMany(p => p.Objects).Select(o => new PlanOccupiedName(o.Kind, o.Name!)).Distinct()];
        return new(snapshot, names, PlanSnapshotHashes.Names(project, names));
    }

    public static PlanFunction[] Functions(PlanBaseline baseline) => [.. baseline.Snapshot.Pages.SelectMany(p => p.Objects)
        .Where(o => o.Kind == "function").Select(o => new PlanFunction { Type = o.Type, Oid = o.Oid, Name = o.Name!, Page = o.Page, IsMain = o.Main!.Value })];
}
