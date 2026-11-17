using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;

public class GraphicEditOperation(IGraphicEditor editor, ITransactionScopeFactory transactions, bool execute) : Operation<JObject, JObject>
{
    public override string Name => execute ? OperationNames.GraphicEditExecute : OperationNames.GraphicEditPlan;
    protected override JObject Execute(JObject args)
    {
        JObject input = execute ? args["request"] as JObject ?? throw GraphicEditPlanner.Invalid("Supply request.") : args;
        JObject before, plan;
        try
        {
            before = editor.Inspect((string?)input["project"] ?? "", (string?)input["page"] ?? "");
            plan = GraphicEditPlanner.Plan(input, before, execute ? (string?)args["fingerprint"] ?? "" : null);
        }
        catch (BridgeErrorException error) { throw WriteArguments.NotStarted(error); }
        if (!execute)
        {
            var scene = before["scene"]!.ToObject<ConnectionCreatePlanner.Scene>()!;
            return (JObject)Protocol.BridgeJson.FromObject(new WritePlan
            {
                Project = scene.Project.Name,
                ProjectPath = scene.Project.Path,
                Fingerprint = (string)plan["fingerprint"]!,
                Changes = (bool?)plan["noOp"] == true ? [] : plan["affected"]!.Select(l => new PlannedChange
                { Action = "edit", Kind = "graphic", Name = scene.Page.Page + ":" + GraphicEditPlanner.Id(l), Property = (string)plan["operation"]!, NewValue = plan["predicted"]!.ToString(Formatting.None) }).ToList(),
                Details = new() { ["preview"] = Public(plan).ToString(Formatting.None) }
            });
        }
        if ((bool?)plan["noOp"] == true)
        { return Public(plan); }
        JObject? after = null;
        Action change = editor.PrepareEdit(input, before, plan);
        void Verify()
        {
            after = editor.Inspect((string)input["project"]!, (string)input["page"]!);
            GraphicEditVerification.Verify(input, before, after, plan);
        }
        Action restored = () => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
        {
            if (!JToken.DeepEquals(before, editor.Inspect((string)input["project"]!, (string)input["page"]!)))
            { throw GraphicEditVerification.Mismatch("Rollback did not restore the page."); }
            return true;
        });
        Committed<bool> result = TransactionRunner.Run(transactions, GraphicEditPlanner.Tool, () => { change(); Verify(); return true; }, _ => true, verifyRollback: restored);
        GraphicEditPlanner.Warn(plan, result.Warning);
        // A99: all reads use freshly resolved objects. A committed read mismatch is a warning, never replayed.
        try
        { Verify(); }
        catch (Exception error) { GraphicEditPlanner.Warn(plan, "Committed readback failed: " + error.Message + " Re-read the page; do not replay the edit."); }
        if (plan["actualRows"] is JArray actual)
        { plan["predicted"] = actual.DeepClone(); }
        if ((string?)input["response_format"] == "detailed")
        { plan["before"] = before; plan["after"] = after; }
        plan["execution"] = ExecutionState.Committed;
        return Public(plan);
    }
    public static JObject Public(JObject plan)
    {
        var value = new Result(plan);
        foreach (string name in new[] { "created", "affected", "segments", "orderTo", "orderKnown", "cutOffResult", "removed", "actualRows", "unverifiedCut", "skipped" })
        { value.Remove(name); }
        return value;
    }
    private sealed class Result(JObject value) : JObject(value), IWriteExecution
    { public string Execution => (string?)this["execution"] ?? ExecutionState.NotStarted; }
}

public sealed class GraphicEditExecuteOperation(IGraphicEditor editor, ITransactionScopeFactory transactions)
    : GraphicEditOperation(editor, transactions, true), IWriteOperation;
