using System.Text;
using System.Text.Json;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;
using EplanMcp.Packs;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Host.Plans;

internal sealed partial class PlanApplyChecks
{
    private async Task<WorkflowEngineering> EngineeringAsync(PlanDefinition plan, ProjectBinding? binding, CancellationToken cancellationToken)
    {
        try
        {
            if (binding?.BindingVersion != 2 || binding.Revision != plan.BindingRevision
                || ContextBuilder.Reference(null, binding)["lockDigest"]?.GetValue<string>() != plan.LockDigest
                || plan.Engineering is not { } approved || approved.Cabinet != plan.Cabinet)
            { throw Stale(); }
            JsonElement workflow = approved.WorkflowRefAndInputs;
            WorkflowEngineering current = await WorkflowEngineering.ReadAsync(packs, binding, plan.Cabinet,
                workflow.GetProperty("workflow").GetString(), workflow.GetProperty("inputs"), cancellationToken).ConfigureAwait(false);
            string digest = PlanStore.Hash(Encoding.UTF8.GetBytes(PlanCanonical.Serialize(JsonSerializer.SerializeToElement(current.Config))));
            if (digest != approved.ConfigDigest || !current.Resources.SequenceEqual(plan.Resources)
                || PlanCanonical.Serialize(JsonSerializer.SerializeToElement(current.Cabinet.Structure)) != PlanCanonical.Serialize(approved.Acceptance.GetProperty("cabinetScope")))
            { throw Stale(); }
            var rules = current.Assets.Where(a => a.Document?.Kind == KnowledgeKind.Rule).ToArray();
            if (rules.Length != approved.RuleDigests.EnumerateObject().Count())
            { throw Stale(); }
            foreach (var rule in rules)
            {
                if (reviews is null || !approved.RuleDigests.TryGetProperty(rule.Id, out JsonElement expected)
                    || expected.GetProperty("sha256").GetString() != rule.Resource.Sha256
                    || await reviews.CheckPlanAsync(rule, current.Assets, cancellationToken).ConfigureAwait(false) != expected.GetProperty("reviewDigest").GetString())
                { throw Stale(); }
            }
            return current;
        }
        catch (Exception ex) when (ex is BridgeCallException or PackFormatException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        { throw Stale(); }
    }

    internal async Task CheckCabinetAsync(PlanDefinition plan, PlanSnapshot snapshot, PlanStep step, CancellationToken cancellationToken)
    {
        if (plan.CompilerVersion != "3")
        { return; }
        ProjectBinding? binding = await ReadBindingAsync(plan, cancellationToken).ConfigureAwait(false);
        WorkflowEngineering current = await EngineeringAsync(plan, binding, cancellationToken).ConfigureAwait(false);
        GuardCabinet(current, binding!, snapshot, [step]);
    }

    private async Task<ProjectBinding?> ReadBindingAsync(PlanDefinition plan, CancellationToken cancellationToken)
    {
        try
        { return await packs.Workspaces.ReadAsync(plan.Project.Path, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (plan.CompilerVersion == "3" && ex is PackFormatException or IOException or UnauthorizedAccessException or JsonException)
        { throw Stale(); }
    }

    private static void GuardCabinet(WorkflowEngineering current, ProjectBinding binding, PlanSnapshot snapshot, IEnumerable<PlanStep> steps)
    {
        try
        { current.Guard(binding, new(snapshot, [], ""), steps); }
        catch (BridgeCallException)
        { throw Stale(); }
    }
}
