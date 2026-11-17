using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Read-only action preview for export and Write entries; no folder creation or action execution.</summary>
public sealed class ActionRunPlanOperation(int major, IProjectCatalog catalog, IActionRunner runner) : Operation<ActionRunArgs, WritePlan>
{
    public override string Name => OperationNames.ActionRunPlan;
    protected override WritePlan Execute(ActionRunArgs args)
    {
        ActionRunRequest request = ActionRunRequest.Check(args, major, catalog, runner);
        return new WritePlan
        {
            Project = request.Project?.Name ?? string.Empty,
            ProjectPath = request.Project?.Path ?? string.Empty,
            Fingerprint = request.StateFingerprint(),
            Changes = [new PlannedChange { Action = "run", Kind = "action", Name = args.Entry, NewValue = JsonConvert.SerializeObject(request.Data.Parameters) }],
            Details = new Dictionary<string, string>
            {
                ["actionRun"] = JsonConvert.SerializeObject(request.Data),
                ["tier"] = (string)request.Entry["tier"]!,
                ["effects"] = (string)request.Entry["effects"]!,
            },
        };
    }
}
