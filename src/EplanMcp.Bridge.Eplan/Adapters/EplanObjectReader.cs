using System.Diagnostics;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A12/A36/A39: finder plus page placements; only detached identities leave this main-thread call.</summary>
internal sealed class EplanObjectReader : IObjectReadAdapter
{
    public IPropertyReadScope Open(string projectPath, PropertyTarget target) => new EplanPropertyReadAdapter().Open(projectPath, target);
    public IReadOnlyList<ObjectSummary> Find(ObjectReadArgs args)
    {
        MainThread.Assert();
        var watch = Stopwatch.StartNew();
        void Check()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            if (watch.Elapsed >= TimeSpan.FromSeconds(20))
            { throw new BridgeErrorException(ErrorReason.Timeout, "Object discovery exceeded 20 seconds. Select one page."); }
        }
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(args.Project!));
        IEnumerable<StorableObject> objects = args.ObjectType switch
        {
            "Page" => finder.GetPages(null),
            "Function" => EplanPropertyReadAdapter.Functions(finder, Check),
            "Connection" => finder.GetConnections(null),
            _ => [],
        };
        var found = new Dictionary<(int, long), ObjectSummary>();
        foreach (StorableObject obj in objects)
        {
            Check();
            if (obj.GetType().Name != args.ObjectType)
            { continue; }
            SnapshotObject identity = EplanPropertyReadAdapter.Describe(obj);
            string page = obj is Page p ? p.Name : identity.Page ?? string.Empty;
            if (args.Page is not null && page != args.Page || args.NameContains is not null
                && (identity.Name ?? string.Empty).IndexOf(args.NameContains, StringComparison.OrdinalIgnoreCase) < 0)
            { continue; }
            found[(identity.Type, identity.Oid)] = new ObjectSummary
            {
                Name = SnapshotValues.Text(identity.Name ?? string.Empty),
                Locator = new DrawingLocator
                {
                    Project = args.Project!,
                    Page = page,
                    Type = identity.Type,
                    Oid = identity.Oid,
                    Digest = SnapshotCanonical.Sha256(Newtonsoft.Json.JsonConvert.SerializeObject(identity))
                },
            };
            if (found.Count > 50000)
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Object discovery exceeds 50000 objects. Select one page."); }
        }
        return found.Values.OrderBy(o => o.Locator.Type).ThenBy(o => o.Locator.Oid).ToList();
    }
}
