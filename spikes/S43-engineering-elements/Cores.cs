using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    internal void AssignCores(string group, Action<string, object> log)
    {
        Page page = RequireGroupPage(group);
        try { AssignCoresOnPage(page, group, log); }
        catch (Exception ex)
        {
            log("cores_failure_page", new { stage = Stage, error = Evidence.Error(ex), page = Diagnose(() => CablePageData(page)) });
            throw;
        }
    }
    private void AssignCoresOnPage(Page page, string group, Action<string, object> log)
    {
        TraceStage("cores.inventory", new { group, page = page.Name });
        log("cores_before", CablePageData(page));
        Cable cable = FindCable(page, group, log);
        Connection[] runs = CableRuns(page, group);
        JObject outside = OtherConnections(runs);
        JObject membership = CableMembership(page, cable, runs);
        log("cores_membership", membership);
        VerifyCableCrossesRuns(cable, runs, group);
        Connection[]? native = cable.CableConnections;
        if ((bool?)membership["boundedIdentitiesMatch"] != true || (bool?)membership["nativeMembershipMatches"] != true ||
            native is not { Length: 3 } || native.Any(c => c is null || c.Page?.ObjectIdentifier != page.ObjectIdentifier || HasForeignCable(c, cable)))
        {
            log("cores_skipped", new { reason = "Committed cable membership is incomplete; no binding or reassignment performed in cores",
                actual = native?.Length, missing = membership["missing"], nextSteps = "bind --group " + group + "; generate --group " + group + "; cores --group " + group,
                projectWideVariantCalled = false });
            throw new InvalidOperationException("Core assignment skipped: inspect cores_membership comparisons; commit bounded bind and generate separately first.");
        }
        Connection[] connections = native.OrderBy(WireX).ToArray();
        if (cable.FunctionTemplates.Length != 0) { throw new InvalidOperationException("Cable already has templates; reset fixture before assigning cores."); }
        if (connections.Select(WireX).Distinct().Count() != 3 || connections.Where((c, i) => !RunEndpoints(c, group, i + 1)).Any() ||
            !connections.Select(c => c.ObjectIdentifier).OrderBy(x => x).SequenceEqual(runs.Select(c => c.ObjectIdentifier).OrderBy(x => x)))
        { throw new InvalidOperationException("Cable cores do not match the three fictional run endpoints."); }
        string[] colors = ["BK", "BU", "GNYE"];
        // Explicit connection locator = ordered run X + original endpoint ids, checked again after assignment.
        var locators = connections.Select(c => (c.ObjectIdentifier, Start: c.StartPin?.ParentFunction?.ObjectIdentifier, End: c.EndPin?.ParentFunction?.ObjectIdentifier)).ToArray();
        for (int i = 0; i < connections.Length; i++)
        {
            Set(connections[i], 31004, colors[i]); // XML: string Connection color / number; 31005 is graphical line color (Int64).
            Set(connections[i], 31011, Evidence.Number(i + 1));
            Set(connections[i], 31002, "1.5");
        }
        TraceStage("cores.add_templates", new { connections = connections.Select(ConnectionData).ToArray() });
        cable.AddLocalFunctionTemplates(connections);
        Connection[] templates = cable.FunctionTemplates.OfType<Connection>().ToArray();
        log("core_templates_readback", new { expected = 3, actual = templates.Length, templates = templates.Select(ConnectionData).ToArray() });
        if (templates.Length != 3 || templates.Any(t => !t.IsTemplate)) { throw new InvalidOperationException("Local core templates not established; no broad reassignment fallback."); }
        log("core_templates", new { cableOid = cable.ObjectIdentifier, templates = templates.Select(ConnectionData).ToArray(), locators });
        try
        {
            TraceStage("cores.reassign", new { cable = ObjectState(cable) });
            using var service = new CableService();
            service.DoReassignWires(cable, templates, true, []); // C18: Function + explicit templates overload ONLY.
        }
        catch (Exception ex) when (ex is BaseException or ArgumentException or ApplicationException)
        {
            log("cores_skipped", new { reason = "Cable-scoped assignment refused; abort this step rather than escalate to project-wide reassignment", error = Evidence.Error(ex), projectWideVariantCalled = false });
            throw;
        }
        Connection[] after = cable.CableConnections.OrderBy(c => WireX(c)).ToArray();
        bool exact = after.Length == 3 && after.Select((c, i) => c.ObjectIdentifier == locators[i].ObjectIdentifier &&
            c.StartPin?.ParentFunction?.ObjectIdentifier == locators[i].Start && c.EndPin?.ParentFunction?.ObjectIdentifier == locators[i].End &&
            Text(c, 31004) == colors[i] && Text(c, 31011) == Evidence.Number(i + 1) &&
            !HasForeignCable(c, cable)).All(x => x);
        bool unchanged = JToken.DeepEquals(outside, OtherConnections(connections));
        LogSnapshot("inside_transaction", Read());
        log("core_assignment_check", new { exact, otherS43ConnectionsUnchanged = unchanged, projectWideVariantCalled = false });
        if (!exact || !unchanged) { throw new InvalidOperationException("Core assignment changed the explicit mapping or another S43 connection; aborting."); }
    }
    private static double WireX(Connection c)
    {
        Pin? pin = c.StartPin ?? c.EndPin;
        if (pin?.ParentFunction is not Function owner) { throw new InvalidOperationException("Core has no explicit function endpoint."); }
        return owner.Location.X + pin.Location.X;
    }
    private JObject OtherConnections(Connection[] selected)
    {
        using var finder = new DMObjectsFinder(project);
        var pages = new HashSet<long>(OwnedPages().Select(p => p.ObjectIdentifier));
        var ids = new HashSet<long>(selected.Select(c => c.ObjectIdentifier));
        return new JObject(finder.GetConnections(null).Where(c => c.Page is not null && pages.Contains(c.Page.ObjectIdentifier) && !ids.Contains(c.ObjectIdentifier))
            .OrderBy(c => c.ObjectIdentifier).Select(c => new JProperty(c.ToStringIdentifier(), ConnectionData(c))));
    }
}
