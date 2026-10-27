using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    internal void BindCable(string group, Action<string, object> log)
    {
        Page page = RequireGroupPage(group);
        try
        {
            Cable cable = FindCable(page, group, log);
            Connection[] runs = CableRuns(page, group);
            JObject outside = OtherConnections(runs);
            JObject before = CableMembership(page, cable, runs);
            log("cable_membership_before_binding", before);
            VerifyCableCrossesRuns(cable, runs, group);
            if (runs.Any(c => HasForeignCable(c, cable) || c.ConnectionDefPoints is not { Length: > 0 } ||
                c.ConnectionDefPoints.Any(p => p?.CableDefinitionLine?.ObjectIdentifier != cable.ObjectIdentifier)))
            { throw new InvalidOperationException("Binding requires each explicit run to have definition points owned only by this cable; generate the committed definition line first."); }
            MatchCableDefinitionNames(page, cable);
            TraceStage("cable.bind_name_parts", new { group, cableOid = cable.ObjectIdentifier,
                runOids = runs.Select(c => c.ObjectIdentifier).ToArray(), projectWideVariantCalled = false });
            using (FunctionBasePropertyList name = cable.NameParts)
            {
                foreach (Connection run in runs) { run.CableNameParts = name; }
            }
            JObject after = CableMembership(page, cable, runs);
            bool exact = (bool?)after["boundedIdentitiesMatch"] == true;
            bool unchanged = JToken.DeepEquals(outside, OtherConnections(runs));
            log("cable_binding_readback", new { exact, otherS43ConnectionsUnchanged = unchanged,
                changes = Evidence.Changes(before, after), membershipPendingCommittedGenerate = (bool?)after["nativeMembershipMatches"] != true,
                nextStep = "After this bind commits, run generate for this group in a separate step, then cores", projectWideVariantCalled = false });
            LogSnapshot("inside_transaction", Read());
            if (!exact || !unchanged) { throw new InvalidOperationException("Bounded cable binding readback differs; inspect cable_membership_before_binding and cable_binding_readback."); }
        }
        catch (Exception ex)
        {
            log("cable_binding_failure", new { stage = Stage, error = Evidence.Error(ex), page = Diagnose(() => CablePageData(page)), beforeRollback = true });
            throw;
        }
    }
    private static Cable FindCable(Page page, string group, Action<string, object> log)
    {
        Cable[] cables = page.AllPlacements.OfType<Cable>().ToArray();
        Cable[] matching = cables.Where(c => c.Name == CableTag(group)).ToArray();
        log("cores_cable_lookup", new { requested = CableTag(group), count = matching.Length,
            available = cables.Select(c => new { c.ObjectIdentifier, c.Name }).ToArray() });
        if (matching.Length != 1)
        { throw new InvalidOperationException("Expected exactly one committed cable " + CableTag(group) + "; inspect cores_cable_lookup and the cable placement result."); }
        return matching[0];
    }
    // No Connection.Cable accessor exists in the catalog. Keep geometric ownership, explicit name and native membership distinct.
    private JObject CableMembership(Page page, Cable cable, Connection[] runs)
    {
        var row = new ReadRecord();
        long? pageId = ReadOid(row, "pageOid", page);
        long? cableId = ReadOid(row, "cableOid", cable);
        string? name = row.Get<string?>("cableName", () => cable.Name);
        string? visible = row.Get<string?>("cableVisibleName", () => cable.VisibleName);
        Connection[]? members = row.Get<Connection[]?>("nativeMembers", () => cable.CableConnections);
        row.Field("pageOid", () => pageId);
        row.Field("cable", () => ObjectState(cable));
        row.Field("nativeMemberCount", () => members?.Length);
        row.Array("nativeMemberOids", () => members, c => new JValue(ReadOid(row, "nativeMemberOid", c)));
        row.Array("runs", () => runs, run =>
        {
            var item = new ReadRecord();
            item.Field("connection", () => ConnectionData(run));
            item.Field("pageMatches", () => pageId is not null && run.Page?.ObjectIdentifier == pageId);
            item.Field("identifyingNameMatches", () => name is not null && Text(run, 20005) == name);
            item.Array("definitionPoints", () => run.ConnectionDefPoints, point =>
            {
                var data = new ReadRecord();
                data.Field("state", () => ObjectState(point));
                data.Field("lineOid", () => point.CableDefinitionLine?.ObjectIdentifier);
                data.Field("ownerMatches", () => cableId is not null && point.CableDefinitionLine?.ObjectIdentifier == cableId);
                data.Field("nameMatches", () => name is not null && point.Name == name);
                data.Field("visibleNameMatches", () => visible is not null && point.VisibleName == visible);
                return data.Data;
            });
            return item.Data;
        });
        JArray? records = row.Data["runs"] as JArray;
        bool bounded = runs.Length == 3 && records?.Count == 3 && records.All(r => r is JObject item &&
            (bool?)item["pageMatches"] == true && (bool?)item["identifyingNameMatches"] == true &&
            item["definitionPoints"] is JArray { Count: > 0 } definitions && definitions.All(d => d is JObject point &&
                (bool?)point["ownerMatches"] == true && (bool?)point["nameMatches"] == true && (bool?)point["visibleNameMatches"] == true));
        long?[] runIds = runs.Select(c => ReadOid(row, "runOid", c)).OrderBy(id => id).ToArray();
        long?[]? memberIds = members?.Select(c => ReadOid(row, "nativeMemberOid", c)).OrderBy(id => id).ToArray();
        bool native = runIds.Length == 3 && runIds.All(id => id is not null) && runIds.Distinct().Count() == 3 &&
            memberIds is { Length: 3 } && runIds.SequenceEqual(memberIds);
        row.Field("boundedIdentitiesMatch", () => bounded);
        row.Field("nativeMembershipMatches", () => native);
        row.Field("missing", () => new[] {
            bounded ? null : "One or more page/connection tag/definition-point owner or name comparisons failed; inspect runs",
            native ? null : "Cable.CableConnections does not enumerate exactly the three explicit run IDs; commit bind then generate separately" }.Where(s => s is not null).ToArray());
        row.Field("connectionCableAccessorAvailable", () => false);
        return row.Data;
    }
}
