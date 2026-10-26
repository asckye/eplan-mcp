using System.Globalization;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S26;

public sealed partial class SpikeS26Action
{
    private static JArray R3Groups(DMObjectsFinder finder)
    {
        // 2026 XML: GetAll<T> returns StorableObject[]. The supplied r2 evidence includes base wrappers for groups.
        // Placement.Group returns a typed borrowed Group; resolve membership through that documented property.
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (Placement placement in finder.GetPlacements(null))
        {
            Group? group = placement as Group ?? placement.Group;
            while (group is not null)
            {
                string id = group.ObjectIdentifier.ToString(CultureInfo.InvariantCulture);
                if (groups.ContainsKey(id)) { break; }
                groups.Add(id, group);
                group = group.Group;
            }
        }
        var rows = new JArray();
        foreach (StorableObject item in finder.GetAll<Group>(true).OrderBy(item => item.ObjectIdentifier))
        {
            var row = new JObject { ["id"] = item.ObjectIdentifier };
            rows.Add(row);
            Group? group = item as Group;
            if (group is null) { groups.TryGetValue(item.ObjectIdentifier.ToString(CultureInfo.InvariantCulture), out group); }
            if (group is null)
            {
                row["knownGap"] = "Group membership unavailable: GetAll<Group> returned a base wrapper not resolved through Placement.Group. Handle retained; not treated as an empty group.";
                row["runtimeType"] = item.GetType().FullName;
            }
            else { Capture(row, "members", () => new JArray(group.SubPlacements.Select(member => member.ObjectIdentifier).OrderBy(id => id))); }
        }
        return rows; // Never dispose borrowed groups/placements; never silently filter untyped handles out.
    }
}
