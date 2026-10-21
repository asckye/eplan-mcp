using System.ComponentModel;
using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tools;

/// <summary>
/// The device write tools (docs/tools/eplan_device_update.md, eplan_device_place.md), through <see cref="WritePipeline"/>
/// like every write: rename a device, and place one by part number.
/// </summary>
[McpServerToolType]
internal sealed class DeviceWriteTools
{
    public const string UpdateTool = "eplan_device_update";

    public const string PlaceTool = "eplan_device_place";

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
        IProgress<ProgressNotificationValue>? progress = null,
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
            PageWriteTools.DefaultLimit,
            "Renaming the device");
        WriteOutcome<DeviceUpdateData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DeviceUpdateArgs, DeviceUpdateArgs, DeviceUpdateData>(request, progress, cancellationToken).ConfigureAwait(false);
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

    [McpServerTool(
        Name = PlaceTool,
        Title = "Place a part's device on an EPLAN page",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DevicePlaceResult))]
    [Description(
        "Places one device by part number on a circuit page, as the engineer would, and gives it the tag; one " +
        "Ctrl+Z in EPLAN undoes it. Several devices: eplan_macro_insert. x, y: mm from the page's bottom left, y up. " +
        "Outside scratch it needs approval. Example: " +
        "{\"project\": \"Line1\", \"page\": \"=GAA+A1&EFS1/60\", \"part\": \"SIE.3RH2122-1HB40\", \"x\": 80, \"y\": 160, \"tag\": \"-KF20\"}.")]
    public static async Task<CallToolResult> DevicePlaceAsync(
        WritePipeline pipeline,
        [Description("From eplan_project_list")] string project,
        [Description("Full circuit page name")] string page,
        [Description("From eplan_part_search")] string part,
        [Description("mm")] double x,
        [Description("mm")] double y,
        [Description("-KF20 (page structure) or full tag")] string tag,
        [Description("Default: 1")] string? variant = null,
        [Description("Only return the plan")] bool dry_run = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // The page, the part, the point and the tag are checked by the Bridge, all at once (device.place.plan).
        if (string.IsNullOrWhiteSpace(project))
        {
            return ToolResults.InvalidArguments(["'project' is required for writes: a name or path from eplan_project_list."]);
        }

        var args = new DevicePlaceArgs { Project = project, Page = page ?? string.Empty, Part = part, Variant = ToolArguments.Optional(variant), X = x, Y = y, Tag = tag };
        var request = new WriteRequest<DevicePlaceArgs, DevicePlaceArgs>(
            PlaceTool,
            ToolTier.Write,
            OperationNames.DevicePlacePlan,
            OperationNames.DevicePlaceExecute,
            args,
            plan => new DevicePlaceArgs { Project = args.Project, Page = args.Page, Part = args.Part, Variant = args.Variant, X = args.X, Y = args.Y, Tag = args.Tag, Fingerprint = plan.Fingerprint },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Placing the device");
        WriteOutcome<DevicePlaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DevicePlaceArgs, DevicePlaceArgs, DevicePlaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        DevicePlaceData? data = outcome.Data;
        var result = new DevicePlaceResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Page = ResultShaper.CleanText(data?.Page ?? outcome.Plan.Changes.FirstOrDefault()?.Name ?? args.Page),
            Part = ResultShaper.CleanText(data?.Part ?? details.GetValueOrDefault("part") ?? part),
            Variant = ResultShaper.CleanText(data?.Variant ?? details.GetValueOrDefault("variant") ?? string.Empty),
            Device = new DevicePlaceResult.DeviceEntry
            {
                Tag = ResultShaper.CleanText(data?.Tag ?? details.GetValueOrDefault("tag") ?? string.Empty),
                VisibleTag = ResultShaper.CleanText(data?.VisibleTag),
                Category = ResultShaper.CleanText(data?.Category),
            },
            Functions = data?.Functions,
            Unplaced = data?.Unplaced,
            Undo = committed ? "AI: " + PlaceTool : null,
            Recovered = outcome.Recovered,
            Hint = PlaceHint(outcome, details),
        };
        string summary = !committed ? "Dry run: would place one device. Nothing was changed."
            : data is null ? "Placed a device; Ctrl+Z in EPLAN undoes this step."
            : $"Placed a device with {ToolResults.Count(data.Functions, "function")}; Ctrl+Z in EPLAN undoes this step.";
        return ToolResults.Success(summary, result);
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? PlaceHint(WriteOutcome<DevicePlaceData> outcome, Dictionary<string, string> details)
    {
        DevicePlaceData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Recovered)
        {
            hints.Add("The response was lost, but EPLAN reported the device as placed; check it with eplan_device_get.");
        }
        else if (data is { Unplaced: > 0 } placed)
        {
            hints.Add($"{ToolResults.Count(placed.Unplaced, "function")} of the device (such as its contacts) are on no page yet; the engineer places them in EPLAN, for example from the device navigator.");
        }

        if (!(data?.PageSizeKnown ?? details.GetValueOrDefault("pageSizeKnown") == "true"))
        {
            hints.Add("EPLAN cannot tell this page's size (a new, empty page), so only x >= 0 and y >= 0 were checked: check in EPLAN that the device lies within the page's frame.");
        }

        if (ResultShaper.CleanText(data?.Warning) is { } warning)
        {
            hints.Add(warning);
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
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
