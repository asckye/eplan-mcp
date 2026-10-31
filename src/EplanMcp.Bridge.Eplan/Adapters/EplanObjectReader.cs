using System.Diagnostics;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;
/// <summary>A12/A36/A39/A60: finder plus page placements; only detached identities leave this main-thread call.</summary>
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
        IEnumerable<StorableObject> objects = Candidates(finder, args.ObjectType.ToLowerInvariant(), Check, args.Page);
        var found = new Dictionary<(int, long), ObjectSummary>();
        foreach (StorableObject obj in objects)
        {
            Check();
            if (!Matches(obj, args.ObjectType.ToLowerInvariant()))
            { continue; }
            SnapshotObject identity = Describe(obj, args.ObjectType.ToLowerInvariant());
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

    internal static IEnumerable<StorableObject> Candidates(DMObjectsFinder finder, string kind, Action check, string? pageName = null)
    {
        check();
        IEnumerable<StorableObject> objects = kind switch
        {
            "page" => finder.GetPages(null),
            "function" => EplanPropertyReadAdapter.Functions(finder, check),
            "connection" => finder.GetConnections(null),
            "terminal" or "plcterminal" => finder.GetTerminals(null),
            "terminalstrip" => finder.GetTerminalStrips(null),
            "plc" => finder.GetPLCs(null),
            "cable" => finder.GetCablesWithFilterScheme(null),
            "blackbox" => finder.GetBoxedDevices(null),
            "placeholder" => finder.GetPlaceholdersWithFilterScheme(null),
            "interruptionpoint" => finder.GetInterruptionPoints(null),
            "potentialdefinition" => finder.GetPotentialDefinitionsWithFilterScheme(null),
            "locationbox" or "macrobox" => Placements(),
            _ => [],
        };
        foreach (StorableObject obj in objects)
        {
            check();
            // Property-get's Function locator also accepts derived function classes, as before B2.
            if (kind == "function" || Matches(obj, kind))
            { yield return obj; }
        }

        IEnumerable<Placement> Placements()
        {
            foreach (Page page in finder.GetPages(null))
            {
                check();
                if (pageName is not null && page.Name != pageName)
                { continue; }
                foreach (Placement placement in page.AllPlacements)
                { check(); yield return placement; }
            }
        }
    }

    internal static bool Matches(StorableObject obj, string kind) => kind switch
    {
        "blackbox" => obj.GetType() == typeof(BoxedDevice),
        "plcterminal" => obj.GetType() == typeof(Terminal) && ((Terminal)obj).FunctionCategory == FunctionCategory.PLCTerminal,
        _ => string.Equals(obj.GetType().Name, kind, StringComparison.OrdinalIgnoreCase),
    };

    internal static SnapshotObject Describe(StorableObject obj, string kind)
    {
        SnapshotObject identity = EplanPropertyReadAdapter.Describe(obj);
        identity.Kind = kind;
        // Page derives from Group (a Placement); its Location throws "page size cannot be determined" (A60).
        if (obj is Placement placement and not Function and not Page)
        {
            identity.Page = placement.Page?.Name;
            identity.Name = obj is FunctionBase function ? function.Name : obj is PlaceHolder holder ? holder.Name : string.Empty;
            PointD position = placement.Location;
            identity.Pos = SnapshotValues.Position(position.X, position.Y);
        }
        return identity;
    }

    // A60: derived lists hide base Properties; TerminalStrip explicitly forbids Function.Properties.
    internal static UniversalPropertyList Properties(StorableObject obj) => obj switch
    {
        TerminalStrip strip => strip.Properties,
        PlugStrip strip => strip.Properties,
        Cable cable => cable.Properties,
        Page page => page.Properties,
        Function function => function.Properties,
        Connection connection => connection.Properties,
        PlaceHolder holder => holder.Properties,
        LocationBox box => box.Properties,
        InterruptionPoint point => point.Properties,
        PotentialDefinition potential => potential.Properties,
        MacroBox box => box.Properties,
        _ => throw new BridgeErrorException(ErrorReason.CapabilityMissing, "This locator kind has no property adapter."),
    };
}
