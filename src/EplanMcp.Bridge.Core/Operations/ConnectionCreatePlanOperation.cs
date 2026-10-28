using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only complete-page preview, retained by WritePipeline for approval and execution.</summary>
public sealed class ConnectionCreatePlanOperation(IConnectionCreator creator) : Operation<JObject, WritePlan>
{
    public override string Name => OperationNames.ConnectionCreatePlan;
    protected override WritePlan Execute(JObject args)
    {
        ConnectionCreatePlanner.Scene scene = creator.Read((string?)args["project"] ?? "", (string?)args["page"] ?? "");
        JObject preview = ConnectionCreatePlanner.Plan(args.ToString(Formatting.None), scene, creator);
        var plan = new WritePlan { Project = scene.Project.Name, ProjectPath = scene.Project.Path, Fingerprint = (string)preview["fingerprint"]! };
        plan.Changes = [new() { Action = "create", Kind = "connections", Name = scene.Page.Page, NewValue = (preview["cableDefinitions"] is null ? "2 undo steps; " : "Cable planning only; execution unavailable; ") + preview.ToString(Formatting.None) }];
        plan.Details = new() { ["preview"] = preview.ToString(Formatting.None) };
        return plan;
    }
}
