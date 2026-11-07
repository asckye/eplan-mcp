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

/// <summary>Renaming structure identifiers (docs/tools/eplan_structure_rename.md). Every page and device using one changes with it, so it is Destructive under ADR-0005.</summary>
[McpServerToolType]
internal sealed class StructureRenameTools
{
    public const string Tool = "eplan_structure_rename";

    [McpServerTool(
        Name = Tool,
        Title = "Rename structure identifiers",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(StructureWriteResult))]
    [Description("Renames structure identifiers; pages and devices using them change too. Previews by default. Execution always requires engineer approval, including scratch, and a project backup. One undo.")]
    public static async Task<CallToolResult> RenameAsync(
        WritePipeline pipeline,
        [Description("Open project")] string project,
        [Description("1-50 renames")] RenameInput[] renames,
        [Description(WriteToolDefaults.Description)] bool dry_run = WriteToolDefaults.Preview,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var args = new StructureRenameArgs
        {
            Project = project,
            Renames = renames is null ? null : [.. renames.Select(r => new StructureRenameValue { Name = r.Name, NewName = r.NewName })],
        };
        var request = new WriteRequest<StructureRenameArgs, StructureRenameArgs>(
            Tool,
            ToolTier.Destructive,
            OperationNames.StructureRenamePlan,
            OperationNames.StructureRenameExecute,
            args,
            (plan, backup) => new StructureRenameArgs { Project = args.Project, Renames = args.Renames, Fingerprint = plan.Fingerprint, Backup = backup },
            dry_run,
            StructureTools.DeleteLimit,
            "Renaming structure identifiers");
        WriteOutcome<StructureRenameData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<StructureRenameArgs, StructureRenameArgs, StructureRenameData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        StructureWriteResult result = StructureTools.Written(outcome.Plan, Tool, outcome.Execution, dry_run, outcome.Recovered, outcome.Data?.Project, outcome.Data?.Warning);
        result.Backup = outcome.Backup ?? outcome.Data?.Backup;
        string count = ToolResults.Count(result.Changes.Count, "structure identifier");
        string summary = result.Changes.Count == 0 ? "Nothing to rename."
            : outcome.Execution == ExecutionState.Committed ? $"Renamed {count}; Ctrl+Z in EPLAN undoes this step."
            : $"Dry run: would rename {count}. Nothing was changed.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>One element of eplan_structure_rename's <c>renames</c>.</summary>
    internal sealed class RenameInput
    {
        [Required]
        [Description("Full name now, such as +S1")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Description("New full name, same block and parent")]
        [JsonPropertyName("new_name")]
        public string NewName { get; set; } = string.Empty;
    }
}
