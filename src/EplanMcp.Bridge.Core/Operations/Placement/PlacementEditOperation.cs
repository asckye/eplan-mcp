using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public class PlacementEditOperation(IPlacementEditor editor, ITransactionScopeFactory transactions, bool execute, Action<OpenProject, BackupTarget?> backup, ISymbolReader? symbols = null) : Operation<JObject, JObject>
{
    public override string Name => execute ? OperationNames.PlacementEditExecute : OperationNames.PlacementEditPlan;
    protected override JObject Execute(JObject args)
    {
        JObject input = execute ? args["request"] as JObject ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["Supply request."])) : args;
        bool symbolEdit = (string?)input["operation"] is "set_variant" or "set_arrangement";
        JObject before, plan;
        try
        { before = editor.Inspect((string?)input["project"] ?? "", (string?)input["page"] ?? "", symbolEdit); plan = symbolEdit ? PlacementSymbolEdit.Plan(input, before, symbols, execute ? (string?)args["fingerprint"] ?? "" : null) : PlacementEditPlanner.Plan(input, before, execute ? (string?)args["fingerprint"] ?? "" : null); }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        if (!execute)
        {
            var scene = before["scene"]!.ToObject<ConnectionCreatePlanner.Scene>()!;
            var changes = (bool)plan["noOp"]! ? new List<PlannedChange>() : plan["predicted"]!.Select(p => new PlannedChange { Action = "edit", Kind = "placement", Name = scene.Page.Page + ":" + PlacementEditPlanner.Id(p["locator"]!), Property = (string)plan["operation"]!, NewValue = "position=" + p["location"]!.ToString(Formatting.None) + "; angle(rad)=" + p["angle"] + "; tag=" + p["properties"]?["name"] }).ToList();
            changes.AddRange(plan["removedConnections"]!.Select(c => new PlannedChange { Action = "delete", Kind = "connection", Name = PlacementEditPlanner.Id(c), OldValue = c.ToString(Formatting.None) }));
            return (JObject)Bridge.Core.Protocol.BridgeJson.FromObject(new WritePlan { Project = scene.Project.Name, ProjectPath = scene.Project.Path, Fingerprint = (string)plan["fingerprint"]!, Changes = changes, Details = new() { ["preview"] = Public(plan).ToString(Formatting.None) } });
        }
        if ((bool)plan["noOp"]!)
        { return Public(plan); }
        JObject? after = null;
        if ((string)plan["operation"]! == "delete")
        { backup(before["scene"]!["project"]!.ToObject<OpenProject>()!, args["backup"]?.ToObject<BackupTarget>()); }
        if (plan["movePlan"] is JObject move)
        { plan["warning"] = PlacementMoveExecution.Execute(editor, transactions, (JObject)plan["moveInput"]!, (string)move["fingerprint"]!, PlacementEditPlanner.Tool)["warning"]?.DeepClone(); }
        else
        {
            // Only align/distribute use the guarded move closure. Copy/rotate/mirror/delete use native edit
            // predictions (B12); their LayoutGuard integration remains deferred by the layout-guard design.
            Action change;
            try
            { change = editor.PrepareEdit(input, before); }
            catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
            void Verify()
            { after = editor.Inspect((string)input["project"]!, (string)input["page"]!, symbolEdit); if (symbolEdit) { PlacementSymbolEdit.Verify(before, after, plan); } else { VerifyResult(before, after, plan); } }
            Action restore = () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() => { if (!JToken.DeepEquals(before, editor.Inspect((string)input["project"]!, (string)input["page"]!, symbolEdit))) { throw Mismatch(); } return true; });
            Committed<bool> result = TransactionRunner.Run(transactions, PlacementEditPlanner.Tool, () => { change(); Verify(); return true; }, _ => true, _ => Verify(), restore);
            plan["warning"] = string.Join(" ", new[] { (string?)plan["warning"], result.Warning }.Where(w => !string.IsNullOrEmpty(w)));
        }
        if ((string?)input["response_format"] == "detailed" && after is not null)
        { plan["before"] = before; plan["after"] = after; }
        plan["execution"] = ExecutionState.Committed;
        return Public(plan);
    }
    private sealed class Result(JObject data) : JObject(data), IWriteExecution { public string Execution => (string?)this["execution"] ?? ExecutionState.NotStarted; }
    public static JObject Public(JObject plan) { var result = new Result(plan); result.Remove("moveInput"); result.Remove("movePlan"); return result; }
    public static void VerifyResult(JObject before, JObject after, JObject plan)
    {
        string op = (string)plan["operation"]!;
        JToken[] predicted = plan["predicted"]!.ToArray(), original = before["objects"]!.ToArray(), actual = after["objects"]!.ToArray();
        var selected = new HashSet<string>(predicted.Select(p => PlacementEditPlanner.Id(p["locator"]!)), StringComparer.Ordinal);
        var remaining = actual.ToList();
        foreach (JToken expected in original.Where(o => op == "copy" || !selected.Contains(PlacementEditPlanner.Id(o["locator"]!))).Concat(op == "delete" ? [] : predicted))
        {
            bool copied = op == "copy" && predicted.Contains(expected);
            JToken? found = remaining.FirstOrDefault(o => (copied ? !original.Any(b => PlacementEditPlanner.Id(b["locator"]!) == PlacementEditPlanner.Id(o["locator"]!)) : PlacementEditPlanner.Id(o["locator"]!) == PlacementEditPlanner.Id(expected["locator"]!)) && EqualShape(expected, o));
            Check(found is not null);
            remaining.Remove(found!);
            if (copied)
            { ((JArray)plan["newLocators"]!).Add(found!["locator"]!.DeepClone()); }
        }
        Check(remaining.Count == 0 && (bool?)after["scene"]!["complete"] == true && JToken.DeepEquals(before["scene"]!["page"]!["pageSize"], after["scene"]!["page"]!["pageSize"]) && JToken.DeepEquals(before["scene"]!["page"]!["gridSize"], after["scene"]!["page"]!["gridSize"]));
        JToken[] functions = before["functions"]!.Where(f => op != "delete" || !selected.Contains(PlacementEditPlanner.Id(f))).ToArray();
        JToken[] added = after["functions"]!.Where(f => !before["functions"]!.Any(b => PlacementEditPlanner.Id(b) == PlacementEditPlanner.Id(f))).ToArray();
        Check(JToken.DeepEquals(new JArray(functions), new JArray(after["functions"]!.Except(added))) && added.Length == (op == "copy" ? predicted.Count(p => (string?)p["kind"] == "Function") : 0)
            && added.All(f => predicted.Any(p => (string?)p["properties"]?["name"] == (string?)f["name"])));
        Check(JToken.DeepEquals(new JArray(before["connections"]!.Where(c => !plan["removedConnections"]!.Any(r => JToken.DeepEquals(r, c)))), after["connections"]));
        plan["newLocators"] = new JArray(plan["newLocators"]!.Distinct(JToken.EqualityComparer));
    }
    private static bool EqualShape(JToken a, JToken b)
    {
        JObject Shape(JToken token)
        { var value = (JObject)token.DeepClone(); foreach (string key in new[] { "locator", "drawing", "exists" }) { value.Remove(key); } return value; }
        bool Equal(JToken x, JToken y) => x.Type is JTokenType.Integer or JTokenType.Float && y.Type is JTokenType.Integer or JTokenType.Float ? Math.Abs((double)x - (double)y) < 1e-6 : x is JProperty xp && y is JProperty yp ? xp.Name == yp.Name && Equal(xp.Value, yp.Value) : x is JContainer xc && y is JContainer yc ? xc.Count == yc.Count && xc.Zip(yc, Equal).All(v => v) : JToken.DeepEquals(x, y);
        return Equal(Shape(a), Shape(b));
    }
    private static BridgeErrorException Mismatch() => new(ErrorReason.EplanOperationFailed, "Placement positions, angles, text, functions or connections differ from the approved edit. Re-read the page.");
    private static void Check(bool valid) { if (!valid) { throw Mismatch(); } }
}
