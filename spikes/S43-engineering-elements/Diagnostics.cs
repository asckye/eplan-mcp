using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    private readonly Dictionary<string, JObject> _lastStates = new(StringComparer.Ordinal);
    private void TraceStage(string stage, object? details = null)
    {
        Stage = stage;
        report("stage", new { stage, details });
    }
    // Diagnostic failures must not hide the original exception or prevent the remaining fields being captured.
    private static JToken? Diagnose(Func<object?> read)
    {
        try { object? value = read(); return value is null ? null : JToken.FromObject(value); }
        catch (Exception ex) { return new JObject { ["readError"] = Evidence.Error(ex) }; }
    }
    private static JObject? ObjectState(StorableObject? obj)
    {
        if (obj is null) { return null; }
        var state = new ReadRecord();
        state.Field("runtimeType", () => obj.GetType().FullName);
        state.Field("valid", () => obj.IsValid);
        state.Field("oid", () => obj.ObjectIdentifier);
        if (obj is FunctionBase named)
        {
            state.Field("name", () => named.Name);
            state.Field("visibleName", () => named.VisibleName);
        }
        if (obj is Function function)
        {
            state.Field("category", () => function.FunctionCategory.ToString());
            FunctionDefinition? definition = state.Get<FunctionDefinition?>("functionDefinition", () => function.FunctionDefinition);
            if (definition is null) { state.Data["definition"] = null; }
            else
            {
                var data = new ReadRecord();
                data.Field("ObjectIdentifier", () => definition.ObjectIdentifier);
                data.Field("Group", () => definition.Group);
                data.Field("Id", () => definition.Id);
                state.Data["definition"] = data.Data;
            }
            state.Field("main", () => function.IsMainFunction);
        }
        return state.Data;
    }
    private JObject? ObjectChanges(StorableObject? obj)
    {
        JObject? state = ObjectState(obj);
        if (state is null) { return null; }
        var read = new ReadRecord();
        string? key = read.Get<string?>("identifier", () => obj?.ToStringIdentifier());
        if (string.IsNullOrEmpty(key))
        {
            read.Field("oid", () => state["oid"]?.DeepClone());
            read.Field("identifier", () => key);
            read.Field("changes", () => state);
            return read.Data;
        }
        _lastStates.TryGetValue(key!, out JObject? previous);
        _lastStates[key!] = state;
        return new JObject { ["oid"] = state["oid"]?.DeepClone(), ["changes"] = new JObject(state.Properties()
            .Where(p => previous is null || !JToken.DeepEquals(previous[p.Name], p.Value)).Select(p => new JProperty(p.Name, p.Value.DeepClone()))) };
    }
    private void FailureState(Exception ex)
    {
        report("failure_state", new { stage = Stage, error = Evidence.Error(ex),
            beforeRollback = true, returned = _returned.Select(r => Diagnose(() => ObjectChanges(r))).ToArray() });
    }
    private void SetInterruptionName(InterruptionPoint point, Page page)
    {
        string plant = Text(page, 1100) ?? string.Empty, location = Text(page, 1200) ?? string.Empty;
        string[] allowed = NameRules.InterruptionForms(plant, location);
        string requested = allowed[0];
        TraceStage("interruption.name.set", new { requested, allowed });
        point.Name = requested;
        string actual = point.Name;
        bool exact = string.Equals(requested, actual, StringComparison.Ordinal);
        bool accepted = NameRules.MatchesInterruption(actual, allowed);
        report("interruption_name_mapping", new { requested, actual, exactNameComparison = exact, comparedAllowedNames = allowed, accepted,
            expectedPlant = plant, actualPlant = Value(point, 1100), expectedLocation = location, actualLocation = Value(point, 1200),
            leaf = Value(point, 20008), visibleName = point.VisibleName, identifyingTag = Value(point, 20005),
            rule = "Compare full Name strings; structure property decomposition is diagnostic only (r2)", state = ObjectChanges(point) });
        if (!accepted) { throw new InvalidOperationException("Interruption Name mismatch; requested=" + requested + "; actual=" + actual); }
    }
    private static bool IsS43Link(InterruptionPoint? point)
    {
        string? name = point?.Name;
        return name == "S43LINK" || name?.EndsWith("-S43LINK", StringComparison.Ordinal) == true;
    }
    private JObject CablePageData(Page page)
    {
        var row = new ReadRecord();
        using var finder = row.Get<DMObjectsFinder?>("finder", () => new DMObjectsFinder(project));
        long? pageId = ReadOid(row, "oid", page);
        Placement[]? placements = row.Get<Placement[]?>("allPlacements", () => page.AllPlacements);
        Connection[]? connections = row.Get<Connection[]?>("sourceConnections", () => finder?.GetConnections(null));
        row.Field("page", () => page.Name);
        row.Array("connections", () => connections?.Where(c => c is null || pageId is not null && row.Get<long?>("connection.page", () => c.Page?.ObjectIdentifier) == pageId)
            .OrderBy(c => ReadOid(row, "connection.oid", c)), ConnectionData);
        row.Array("cables", () => UniqueRead(row, "cables", (placements ?? []).OfType<Cable>().Concat(_returned.OfType<Cable>()
            .Where(c => pageId is not null && ReadPageOid(row, "cable.page", c) == pageId))), cable =>
        {
            var item = new ReadRecord();
            item.Field("state", () => ObjectState(cable));
            item.Field("type20040", () => Value(cable, 20040));
            item.Field("count20042", () => Value(cable, 20042));
            item.Field("crossSection20043", () => Value(cable, 20043));
            item.Array("cableConnections", () => cable.CableConnections, ConnectionData);
            item.Field("location", () => XY(cable.Location));
            item.Field("graphicalConnectionCount", () => cable.GraphicalConnections?.Length);
            item.Array("graphicalConnections", () => cable.GraphicalConnections, connection =>
            {
                var data = new ReadRecord();
                data.Field("startOid", () => connection.StartSymbolRef?.ObjectIdentifier);
                data.Field("endOid", () => connection.EndSymbolRef?.ObjectIdentifier);
                return data.Data;
            });
            var definitions = (placements ?? []).OfType<ConnectionDefinitionPoint>().Concat((connections ?? [])
                .Where(c => c is not null && pageId is not null && row.Get<long?>("definition.connectionPage", () => c.Page?.ObjectIdentifier) == pageId)
                .SelectMany(c => item.Get<ConnectionDefinitionPoint[]?>("connection.definitionPoints", () => c.ConnectionDefPoints) ?? []));
            long? cableId = ReadOid(item, "cable.oid", cable);
            item.Array("ownedDefinitionPoints", () => UniqueRead(item, "definitionPoints", definitions)
                .Where(p => cableId is not null && item.Get<long?>("definition.lineOid", () => p.CableDefinitionLine?.ObjectIdentifier) == cableId), ObjectState);
            return item.Data;
        });
        return row.Data;
    }
}
