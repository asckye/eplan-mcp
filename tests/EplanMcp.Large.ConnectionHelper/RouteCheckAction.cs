using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>
/// Connection length spike (owner 2026-11-14, data-relationships R5): in one installation space of the sample fixture,
/// reads every 3D connection's routed path, clears and routes the routed ones again with their own routing type, reads again and
/// rolls the transaction back. Equal lengths mean routing is deterministic, so a reproduced 3D layout can reproduce the
/// sample's connection lengths; different lengths mean it cannot, whatever the layout.
/// </summary>
public sealed class RouteCheckAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeRouteCheck"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            using var locking = new LockingStep();
            MainThread.Use(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            JObject input = JObject.Parse(File.ReadAllText(file));
            Project project = new SelectionSet().GetCurrentProject(false);
            string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
            if (!string.Equals(project.ProjectFullName, Path.Combine(home, "scratch", "fixtures", "EPLAN_Sample_Project"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("Only the current scratch sample fixture is permitted."); }

            string want = (string?)input["space"] ?? throw new ArgumentException("space is required.");
            InstallationSpace space = project.InstallationSpaces.FirstOrDefault(sp => sp.Properties[36452].ToString() == want)
                ?? throw new ArgumentException("No installation space " + want + ".");

            Dictionary<string, JObject> before = Snapshot(space);
            result["before"] = before.Count;
            using var service = new ConnectionService3D();
            var routed = new JObject();
            using (var scope = new TransactionScope("Route check"))
            {
                // Clear every routed path first: a router that skips connections already routed would otherwise keep
                // every length and prove nothing. The cleared snapshot shows the clearing took.
                var groups = space.Connection3Ds
                    .Select(c => (Connection: c, Type: Routed(c)))
                    .Where(c => c.Type is { } type && type != Connection3D.Enums.RoutingType.Undefined)
                    .GroupBy(c => c.Type!.Value, c => c.Connection)
                    .ToList();
                foreach (Connection3D connection in groups.SelectMany(g => g))
                { connection.ClearRouteAndNodes(); }

                result["cleared"] = Compare(before, Snapshot(space));
                // Route each group with the type it was routed with, so the check changes nothing but the act of routing.
                foreach (IGrouping<Connection3D.Enums.RoutingType, Connection3D> group in groups)
                {
                    service.RouteConnections([.. group], group.Key);
                    routed[group.Key.ToString()] = group.Count();
                }

                Dictionary<string, JObject> after = Snapshot(space);
                result["after"] = Compare(before, after);
                scope.Rollback();
            }

            result["routedGroups"] = routed;
            result["rolledBack"] = Compare(before, Snapshot(space));
            result["success"] = true;
        }
        catch (Exception error)
        {
            result["error"] = error.GetType().FullName + ": " + error.Message;
        }

        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    /// <summary>Each 3D connection by its two ends, with its routed path length in millimetres.</summary>
    private static Dictionary<string, JObject> Snapshot(InstallationSpace space)
    {
        var rows = new Dictionary<string, JObject>(StringComparer.Ordinal);
        foreach (Connection3D connection in space.Connection3Ds)
        {
            string key = End(connection, start: true) + " > " + End(connection, start: false);
            var row = new JObject();
            try
            {
                PointD3D[] points = connection.RoutePoints ?? [];
                double length = 0;
                for (int i = 1; i < points.Length; i++)
                {
                    double dx = points[i].X - points[i - 1].X, dy = points[i].Y - points[i - 1].Y, dz = points[i].Z - points[i - 1].Z;
                    length += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                }

                row["routed"] = connection.IsRouted;
                row["type"] = connection.RoutingType.ToString();
                row["points"] = points.Length;
                row["length"] = Math.Round(length, 3);
            }
            catch (NotSupportedException error)
            {
                row["routed"] = false;
                row["length"] = 0;
                row["error"] = error.Message;
            }

            // The same two ends can carry several connections; number them in enumeration order.
            string unique = key;
            for (int n = 2; rows.ContainsKey(unique); n++)
            { unique = key + " #" + n; }

            rows[unique] = row;
        }

        return rows;
    }

    /// <summary>
    /// An end as its device and pin. Live 2026-11-14: where the end is a connection point position, StartPin/EndPin throw
    /// S063219 and name StartConnectionPointPosition instead, so that position's device tag and index stand in.
    /// </summary>
    private static string End(Connection3D connection, bool start)
    {
        string? device = Read(() => (start ? connection.StartObject : connection.EndObject)?.Name);
        string? pin = Read(() => (start ? connection.StartPin : connection.EndPin)?.Name);
        pin ??= Read(() =>
        {
            using ConnectionPointPosition? position = start ? connection.StartConnectionPointPosition : connection.EndConnectionPointPosition;
            return position is null ? null : position.DeviceTag + "#" + position.Index;
        });
        return (device ?? "?") + ":" + (pin ?? "?");
    }

    /// <summary>The routing type of a routed connection; null when it is not routed or the members cannot be read.</summary>
    private static Connection3D.Enums.RoutingType? Routed(Connection3D connection)
    {
        try
        { return connection.IsRouted ? connection.RoutingType : null; }
        catch (NotSupportedException)
        { return null; }
    }

    private static string? Read(Func<string?> member)
    {
        try
        { return member(); }
        catch (NotSupportedException)
        { return null; }
    }

    /// <summary>How many connections kept their length within 0.5 mm, and the ones that did not.</summary>
    private static JObject Compare(Dictionary<string, JObject> before, Dictionary<string, JObject> after)
    {
        var changed = new JArray();
        int same = 0;
        foreach (KeyValuePair<string, JObject> pair in before)
        {
            (string key, JObject old) = (pair.Key, pair.Value);
            if (!after.TryGetValue(key, out JObject? now))
            { changed.Add(new JObject { ["connection"] = key, ["missing"] = true }); continue; }

            double delta = (double)now["length"]! - (double)old["length"]!;
            if (Math.Abs(delta) <= 0.5 && (bool)now["routed"]! == (bool)old["routed"]!)
            { same++; }
            else if (changed.Count < 50)
            { changed.Add(new JObject { ["connection"] = key, ["before"] = old, ["after"] = now }); }
        }

        return new JObject { ["compared"] = before.Count, ["same"] = same, ["added"] = after.Keys.Except(before.Keys).Count(), ["changed"] = changed };
    }
}
