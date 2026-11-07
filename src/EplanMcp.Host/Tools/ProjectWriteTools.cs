using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>Setting a project's title block properties (ADR-0052, docs/tools/eplan_project_update.md), through <see cref="WritePipeline"/> like every write.</summary>
[McpServerToolType]
internal sealed class ProjectWriteTools
{
    public const string UpdateTool = "eplan_project_update";

    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    [McpServerTool(
        Name = UpdateTool,
        Title = "Set the title block properties of an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ProjectUpdateResult))]
    [Description("Sets title block project properties: description, company, address, creator. Previews by default. One undo; approval outside scratch.")]
    public static async Task<CallToolResult> UpdateAsync(
        WritePipeline pipeline,
        [Description("Open project")] string project,
        [Description("Properties to set")] ProjectPropertyInput[] properties,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var args = new ProjectUpdateArgs
        {
            Project = project,
            Properties = properties is null ? null : [.. properties.Select(p => new ProjectPropertyValue { Id = p.Id ?? 0, Name = p.Name, Value = p.Value })],
        };
        var request = new WriteRequest<ProjectUpdateArgs, ProjectUpdateArgs>(
            UpdateTool,
            ToolTier.Write,
            OperationNames.ProjectUpdatePlan,
            OperationNames.ProjectUpdateExecute,
            args,
            plan => new ProjectUpdateArgs { Project = args.Project, Properties = args.Properties, Fingerprint = plan.Fingerprint },
            dry_run,
            Limit,
            "Setting project properties");
        WriteOutcome<ProjectUpdateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<ProjectUpdateArgs, ProjectUpdateArgs, ProjectUpdateData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new ProjectUpdateResult
        {
            Project = ResultShaper.CleanText(outcome.Data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Fingerprint = outcome.Plan.Fingerprint,
            Changes = [.. outcome.Plan.Changes.Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = ResultShaper.CleanText(c.Name), Property = c.Property, OldValue = ResultShaper.CleanText(c.OldValue), NewValue = ResultShaper.CleanText(c.NewValue) })],
            Undo = committed ? "AI: " + UpdateTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the change as committed; check the properties with eplan_project_get."
                : dry_run ? WriteToolDefaults.ExecutionHint : ResultShaper.CleanText(outcome.Data?.Warning),
        };
        string count = ToolResults.Count(result.Changes.Count, "property");
        string summary = result.Changes.Count == 0 ? "Nothing to change: the project already has these values."
            : committed ? $"Set {count}; Ctrl+Z in EPLAN undoes this step."
            : $"Dry run: would set {count}. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>One element of eplan_project_update's <c>properties</c>.</summary>
    internal sealed class ProjectPropertyInput
    {
        [Description("Property number; list in eplan_tool_usage. Omit when name is given")]
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [Description("Template property by identifying name, ESS.Project.*")]
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [Required]
        [Description("New text, max 500 chars; empty clears; 10011/10014/10030/10031/10032 also take zh_CN@text;en_US@text;")]
        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;
    }
}
