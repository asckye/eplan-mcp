using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The device write tools (docs/tools/eplan_device_update.md), through <see cref="WritePipeline"/> like every write.
/// This version renames a device; its properties follow in M3b (M3 design 4.4).
/// </summary>
[McpServerToolType]
internal sealed class DeviceWriteTools
{
    public const string UpdateTool = "eplan_device_update";

    [McpServerTool(
        Name = UpdateTool,
        Title = "Rename a device in an EPLAN project",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DeviceUpdateResult))]
    [Description(
        "Renames a device in an EPLAN project: its main function and every auxiliary function, also contacts on other " +
        "pages, get the new tag in one step that one Ctrl+Z in EPLAN undoes. Outside scratch it needs the engineer's " +
        "approval. Example: {\"project\": \"Line1\", \"tag\": \"=F+A2-KF1\", \"new_tag\": \"-KF19\"}.")]
    public static async Task<CallToolResult> DeviceUpdateAsync(
        WritePipeline pipeline,
        [Description("Name or path from eplan_project_list")] string project,
        [Description("Current tag, full or as shown, e.g. -KF1")] string tag,
        [Description("Full new tag, or from '-' to keep the structure")] string new_tag,
        [Description("Only return the plan")] bool dry_run = false,
        CancellationToken cancellationToken = default)
    {
        // The tags are checked by the Bridge, all at once (device.update.plan).
        if (string.IsNullOrWhiteSpace(project))
        {
            return ToolResults.InvalidArguments(["'project' is required for writes: a name or path from eplan_project_list."]);
        }

        var args = new DeviceUpdateArgs { Project = project, Tag = tag ?? string.Empty, NewTag = new_tag ?? string.Empty };
        var request = new WriteRequest<DeviceUpdateArgs, DeviceUpdateArgs>(
            UpdateTool,
            ToolTier.Write,
            OperationNames.DeviceUpdatePlan,
            OperationNames.DeviceUpdateExecute,
            args,
            plan => new DeviceUpdateArgs { Project = args.Project, Tag = args.Tag, NewTag = args.NewTag, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.DefaultLimit);
        WriteOutcome<DeviceUpdateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DeviceUpdateArgs, DeviceUpdateArgs, DeviceUpdateData>(request, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        var result = new DeviceUpdateResult
        {
            Project = ResultShaper.CleanText(outcome.Data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Device = outcome.Data is { } data ? Device(data) : Planned(outcome.Plan),
            Undo = committed ? "AI: " + UpdateTool : null,
            Recovered = outcome.Recovered,
            Hint = outcome.Recovered
                ? "The response was lost, but EPLAN reported the rename as committed; check the device with eplan_device_get."
                : ResultShaper.CleanText(outcome.Data?.Warning),
        };
        string what = $"a device and its {ToolResults.Count(Math.Max(0, result.Device.Functions - 1), "auxiliary function")}";
        return ToolResults.Success(
            committed ? $"Renamed {what}; Ctrl+Z in EPLAN undoes this step." : $"Dry run: would rename {what}. Nothing was changed.",
            result);
    }

    private static DeviceUpdateResult.DeviceEntry Device(DeviceUpdateData data) => new()
    {
        Tag = ResultShaper.CleanText(data.Tag),
        PreviousTag = ResultShaper.CleanText(data.PreviousTag),
        VisibleTag = ResultShaper.CleanText(data.VisibleTag),
        Functions = data.Functions,
        Pages = [.. data.Pages.Select(p => ResultShaper.CleanText(p))],
    };

    /// <summary>The device as the plan has it: the change and the details device.update.plan adds.</summary>
    private static DeviceUpdateResult.DeviceEntry Planned(WritePlan plan)
    {
        PlannedChange? change = plan.Changes.FirstOrDefault();
        Dictionary<string, string> details = plan.Details ?? [];
        return new DeviceUpdateResult.DeviceEntry
        {
            Tag = ResultShaper.CleanText(change?.NewValue ?? string.Empty),
            PreviousTag = ResultShaper.CleanText(change?.OldValue ?? string.Empty),
            Functions = int.TryParse(details.GetValueOrDefault("functions"), NumberStyles.None, CultureInfo.InvariantCulture, out int functions) ? functions : 0,
            Pages = details.GetValueOrDefault("pages") is { Length: > 0 } pages ? [.. pages.Split('\n').Select(p => ResultShaper.CleanText(p))] : [],
        };
    }
}
