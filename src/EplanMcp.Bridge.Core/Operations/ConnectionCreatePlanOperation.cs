using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Complete-page drawing preview; the closed moves branch shares this entry without adding operations.</summary>
public sealed class ConnectionCreatePlanOperation(IConnectionCreator creator) : Operation<JObject, WritePlan>
{
    public override string Name => OperationNames.ConnectionCreatePlan;
    protected override WritePlan Execute(JObject args)
    {
        ConnectionCreatePlanner.Scene scene = creator.Read((string?)args["project"] ?? "", (string?)args["page"] ?? "");
        bool moving = args.ContainsKey("moves");
        if (moving)
        { scene.SymbolBounds.Clear(); }
        JObject preview = moving ? PlacementMovePlanner.Plan(args.ToString(Formatting.None), scene) : ConnectionCreatePlanner.Plan(args.ToString(Formatting.None), scene, creator);
        var plan = new WritePlan { Project = scene.Project.Name, ProjectPath = scene.Project.Path, Fingerprint = (string)preview["fingerprint"]! };
        plan.Changes = moving && (bool)preview["noOp"]! ? [] : [new() { Action = moving ? "move" : "create", Kind = moving ? "placements" : "connections", Name = scene.Page.Page, NewValue = (moving ? "1 undo step; " : preview["cableDefinitions"] is null ? "2 undo steps; " : "Cable planning only; execution unavailable; ") + preview.ToString(Formatting.None) }];
        plan.Details = new() { ["preview"] = preview.ToString(Formatting.None) };
        return plan;
    }
}
