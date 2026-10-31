using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Independent exclusions and explicit current-project guard before any trial inspection.</summary>
public sealed class ActionTryPlanOperation(IProjectCatalog catalog, IActionRunner runner) : Operation<ActionTryArgs, WritePlan>
{
    public override string Name => OperationNames.ActionTryPlan;
    internal static ActionRunRequest Check(ActionTryArgs args, IProjectCatalog catalog, IActionRunner runner)
    {
        if (!ActionTryPolicy.Name(args.Action) || ActionTryPolicy.Denied("action", args.Action + "/" + args.Type)
            || args.Type is not null && !ActionTryPolicy.Name(args.Type)
            || ActionRunRequest.Document()["entries"]!.Any(e => string.Equals((string?)e["action"], args.Action, StringComparison.OrdinalIgnoreCase)
                && string.Equals((string?)e["fixed"]?["TYPE"], args.Type, StringComparison.OrdinalIgnoreCase)))
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "This action is excluded from trials."); }
        if (string.IsNullOrWhiteSpace(args.Project) || args.Parameters is null || args.Parameters.Count > 32
            || args.Parameters.Any(p => p is null || !ActionTryPolicy.Name(p.Name) || p.Name.Equals("TYPE", StringComparison.OrdinalIgnoreCase)
                || ActionTryPolicy.Denied("parameter", p.Name) || !ActionTryPolicy.Value(p.Value))
            || args.Parameters.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        { throw WriteArguments.Invalid(["Supply project and unique simple trial parameters; target overrides are forbidden."]); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        if (!project.Current || catalog.ListOpenProjects().Count != 1)
        { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Only the target scratch project may be open and current for a trial."); }
        var parameters = args.Parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        if (args.Type is not null)
        { parameters.Add("TYPE", args.Type); }
        string entry = "try:" + args.Action + "/" + args.Type;
        return new ActionRunRequest(new JObject { ["entry"] = entry, ["action"] = args.Action, ["seconds"] = 30, ["effects"] = "unverified; rollback and undo are unverified." },
            project, runner.Inspect(entry, project.Path, parameters));
    }
    protected override WritePlan Execute(ActionTryArgs args)
    {
        ActionRunRequest request = Check(args, catalog, runner);
        return new WritePlan
        {
            Project = request.Project!.Name,
            ProjectPath = request.Project.Path,
            Fingerprint = request.StateFingerprint(),
            Changes = [new PlannedChange { Action = "try", Kind = "action", Name = args.Action, NewValue = JsonConvert.SerializeObject(request.Data.Parameters) }],
            Details = new() { ["unverified"] = "true", ["effects"] = "unverified; TYPE=" + args.Type + "; rollback and undo are unverified.", ["actionRun"] = JsonConvert.SerializeObject(request.Data) },
        };
    }
}
