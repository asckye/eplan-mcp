using System.ComponentModel;
using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Packs;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Packs;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Read-only compilation for the full profile's workflow toolset.</summary>
[McpServerToolType]
internal sealed class WorkflowTools
{
    public const string PlanTool = "eplan_workflow_plan";

    [McpServerTool(Name = PlanTool, Title = "Compile workflow plan", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Compiles inline steps or a locked workflow with cabinet configuration, reviewed rules and sources. Multiple cabinets require cabinet. Changes no project data. Example: {\"project\":\"Line1\",\"steps\":[{\"op\":\"page.create\",\"args\":{\"pages\":[{\"name\":\"+A1/60\"}]}}]}.")]
    public static async Task<CallToolResult> PlanAsync(IBridgeClient bridge, PackLibrary packs, PlanStore store,
        PlanTrust trust, TaskRegistry sessions, McpServer server, string project, JsonElement? steps = null,
        string? cabinet = null, string? workflow = null, JsonElement? inputs = null, KnowledgeReviewService? reviews = null,
        IProgress<ProgressNotificationValue>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            PlanOwner owner = sessions.OwnerFor(server);
            if (steps.HasValue == (workflow is not null) || workflow is null && inputs.HasValue)
            { return ToolResults.InvalidArguments(["/workflow: supply either steps or workflow with optional inputs."]); }
            PlanArgumentCheck input;
            if (workflow is not null)
            {
                using JsonDocument? supplied = inputs.HasValue ? JsonDocument.Parse(inputs.Value.GetRawText(), new JsonDocumentOptions { AllowDuplicateProperties = false }) : null;
                if (string.IsNullOrWhiteSpace(project))
                { return ToolResults.InvalidArguments(["/project: specify an open project."]); }
                PlanNamesData names = await new PlanNamesReader(bridge).ReadAsync(project, cancellationToken).ConfigureAwait(false);
                ProjectBinding? binding = await ContextBuilder.ReadBindingAsync(packs, PlanSnapshotHashes.ProjectKey(names.ProjectPath), cancellationToken).ConfigureAwait(false);
                if (binding?.BindingVersion != 2)
                { throw ContextBuilder.Missing("workflow:v2_binding"); }
                input = (await WorkflowEngineering.ReadAsync(packs, binding, cabinet, workflow, inputs ?? default, cancellationToken).ConfigureAwait(false)).Expand(project);
            }
            else
            {
                input = PlanArguments.Check(JsonSerializer.SerializeToElement(new
                {
                    project,
                    steps,
                }), null, "");
            } // no language: EPLAN's interface language, as the page tools; the planned changes name it
            if (!input.Violations.IsEmpty)
            {
                return WorkflowResults.Error(ErrorReason.InvalidArgument, "Correct the listed arguments and compile again.",
                    input.Metadata);
            }
            if (input.Steps.Any(s => s.Op == "device.renumber" && s.Args.GetProperty("scheme").GetString() == ""))
            {
                return WorkflowResults.Error(ErrorReason.CapabilityMissing, "Supply an explicit device numbering scheme and compile again.", new
                {
                    missing = (string[])["plan:default_device_scheme"],
                });
            }
            using BridgeProgress? reporting = BridgeProgress.Start(CallProgress.For(progress), "Compiling workflow plan", packs.Clock, cancellationToken);
            (PlanDefinition definition, Dictionary<string, byte[]> evidence) = await WorkflowCompilation.CompileAsync(bridge, packs, input, project, owner, reviews, cabinet, workflow, inputs ?? default, cancellationToken).ConfigureAwait(false);
            string changes = PlanDiffPreview.For(definition.Steps, local: true);
            definition = definition with
            {
                Digests = definition.Digests with
                {
                    ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(changes)),
                },
            };
            PlanRecord record = await store.PublishAsync(definition, changes, evidence, cancellationToken).ConfigureAwait(false);
            _ = trust.Register(record.Definition);
            return WorkflowResults.Success(record);
        }
        catch (TaskRegistryException ex)
        {
            return WorkflowResults.Error(ex.Error.Reason, ex.Error.Message, ex.Error.Metadata);
        }
        catch (BridgeCallException ex)
        {
            if (ex.Error.Reason == ErrorReason.MacroNotFound)
            {
                return WorkflowResults.Error(ErrorReason.ResourceNotFound, ex.Error.Message, ex.Error.Metadata);
            }
            return WorkflowResults.BridgeError(ex.Error);
        }
        catch (WorkflowCompilationException ex)
        {
            return WorkflowResults.Error(ex.Reason, ex.Message, ex.Metadata);
        }
        catch (PlanCapacityException ex)
        {
            return WorkflowResults.Error(ex.Reason, ex.Message, ex.Metadata);
        }
        catch (AuditUnavailableException)
        {
            return WorkflowResults.Error(ErrorReason.AuditUnavailable, "The audit log is unavailable; no plan was published. Ask the engineer to run 'eplanmcp doctor'.");
        }
        catch (JsonException) when (workflow is not null)
        { return ToolResults.InvalidArguments(["/inputs: supply unique members with typed scalar values."]); }
        catch (PackFormatException ex) when (workflow is not null)
        { return ToolResults.InvalidArguments([ResultShaper.CleanText(ex.Message)]); }
        catch (PackFormatException)
        {
            return WorkflowResults.Error(ErrorReason.CapabilityMissing, "Repair the workspace binding before compiling again.", new
            {
                missing = (string[])["workspace:valid_binding"],
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WorkflowResults.Error(ErrorReason.Internal, "Plan storage is unavailable. Ask the engineer to run 'eplanmcp doctor' before compiling again.");
        }
    }
}
