using System.Globalization;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Potential definition properties as eplan_connection_create writes them on creation: names through the object,
/// colour (31004), cross-section (31002) and potential value (33003) as text, potential type (31006) as an integer.
/// </summary>
internal sealed class EplanPotentialEditor : IPotentialEditor
{
    private static readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal)
    {
        ["color"] = 31004,
        ["cross_section"] = 31002,
        ["potential_value"] = 33003,
        ["potential_type"] = 31006,
    };

    public IReadOnlyList<IReadOnlyDictionary<string, string?>> Read(string project, string page, IReadOnlyList<DrawingLocator> items, IReadOnlyList<string> names)
    {
        MainThread.Assert();
        return [.. Select(project, page, items).Select(point => (IReadOnlyDictionary<string, string?>)Values(point, names))];
    }

    public Action Prepare(string project, string page, IReadOnlyList<(DrawingLocator Locator, IReadOnlyDictionary<string, string> Values)> changes)
    {
        MainThread.Assert();
        _ = Select(project, page, [.. changes.Select(c => c.Locator)]);
        return () =>
        {
            PotentialDefinition[] points = Select(project, page, [.. changes.Select(c => c.Locator)]);
            for (int i = 0; i < changes.Count; i++)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                Write(points[i], changes[i].Values);
            }
        };
    }

    // A86/api-notes: take Properties once per object and dispose it once. Taking it again for each property and disposing each
    // copy corrupted EPLAN's heap (0xc0000374) in the first live run of this tool (2026-11-10).
    private static Dictionary<string, string?> Values(PotentialDefinition point, IReadOnlyList<string> names)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        using UniversalPropertyList properties = point.Properties;
        foreach (string name in names)
        {
            if (name == "potential_name")
            { values[name] = point.PotentialName; continue; }
            if (name == "signal_name")
            { values[name] = point.SignalName; continue; }
            using AnyPropertyId id = _ids[name];
            using PropertyValue value = properties[id];
            values[name] = value.IsEmpty ? null : name == "potential_type" ? value.ToInt().ToString(CultureInfo.InvariantCulture) : value.ToString();
        }
        return values;
    }

    private static void Write(PotentialDefinition point, IReadOnlyDictionary<string, string> changes)
    {
        if (changes.TryGetValue("potential_name", out string? potential))
        { point.PotentialName = potential; }
        if (changes.TryGetValue("signal_name", out string? signal))
        { point.SignalName = signal; }
        if (!changes.Keys.Any(_ids.ContainsKey))
        { return; }
        using UniversalPropertyList properties = point.Properties;
        foreach (KeyValuePair<string, string> change in changes.Where(c => _ids.ContainsKey(c.Key)))
        {
            using AnyPropertyId id = _ids[change.Key];
            if (change.Key == "potential_type")
            { properties[id] = int.Parse(change.Value, CultureInfo.InvariantCulture); }
            else
            { properties[id] = change.Key == "cross_section" ? change.Value.Replace('.', ',') : change.Value; }
        }
    }

    private static PotentialDefinition[] Select(string project, string page, IReadOnlyList<DrawingLocator> items)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        Placement[] all = EplanDeviceReader.FindPage(finder, page)?.AllPlacements
            ?? throw new BridgeErrorException(ErrorReason.PlanStale, "The page disappeared; read it again.");
        return [.. items.Select(l => all.FirstOrDefault(p => p.ObjectIdentifier == l.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == l.Type) as PotentialDefinition
            ?? throw new BridgeErrorException(ErrorReason.PlanStale, "A potential definition disappeared or is no longer one; read the page again."))];
    }
}
