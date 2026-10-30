using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
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
        if (ToolArguments.WriteProjectViolations(project) is { Count: > 0 } violations)
        {
            return ToolResults.InvalidArguments(violations);
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
        Title = "Place a device on an EPLAN page",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DevicePlaceResult))]
    [Description(
        "Places a part device, library symbol (including NO/NC contacts or PLC), terminal with its strip, or dashed location box; Ctrl+Z undoes it. " +
        "Devices inside a location box keep their own structure; no devices are renamed. Sources are exclusive. x, y: mm from bottom left, y up. Outside scratch needs approval. Example: " +
        "{\"project\": \"Line1\", \"page\": \"=GAA+A1&EFS1/60\", \"part\": \"SIE.3RH2122-1HB40\", \"x\": 80, \"y\": 160, \"tag\": \"-KF20\"}.")]
    public static async Task<CallToolResult> DevicePlaceAsync(
        WritePipeline pipeline,
        IBridgeClient bridge,
        [Description("Project")] string project,
        [Description("Circuit page")] string page,
        [Description("mm")] double x,
        [Description("mm")] double y,
        [Description("Device tag")] string? tag = null,
        [Description("From eplan_part_search")] string? part = null,
        [Description("Default: 1")] string? variant = null,
        [Description("Only return the plan")] bool dry_run = false,
        [Description("Project library")] string? library = null,
        [Description("Symbol name")] string? symbol = null,
        [Description("Symbol variant")] int? symbol_variant = null,
        [Description("Relative displays")] JsonElement[]? display_properties = null,
        [Description("Pin designations in order")] string[]? connection_designations = null,
        [Description("Exclusive +location, e.g. +P1")] string? location_box = null,
        [Description("Box corner x, mm")] double? corner_x = null,
        [Description("Box corner y, mm")] double? corner_y = null,
        [Description("Strip tag, e.g. -X6")] string? terminal_strip = null,
        [Description("Terminal number")] string? terminal_number = null,
        [Description("New strip text (??_??) only")] string? terminal_strip_text = null,
        [Description("PLC point suffix; tag is existing card")] string? plc_connection_point = null,
        [Description("PLC I/O address, e.g. I0.0")] string? plc_address = null,
        [Description("PLC manual symbolic address")] string? plc_symbolic_address = null,
        [Description("PLC function text (??_??)")] string? plc_text = null,
        [Description(PageDisplay.Description)] bool show_page = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var args = new DevicePlaceArgs
        {
            Project = project,
            Page = page ?? string.Empty,
            Part = part,
            Variant = ToolArguments.Optional(variant),
            X = x,
            Y = y,
            Tag = tag,
            Library = library?.Trim(),
            Symbol = symbol?.Trim(),
            SymbolVariant = symbol_variant,
            ConnectionDesignations = connection_designations,
            LocationBox = location_box,
            CornerX = corner_x,
            CornerY = corner_y,
            TerminalStrip = terminal_strip,
            TerminalNumber = terminal_number,
            TerminalStripText = terminal_strip_text,
            PlcConnectionPoint = plc_connection_point,
            PlcAddress = plc_address,
            PlcSymbolicAddress = plc_symbolic_address,
            PlcText = plc_text
        };
        List<string> violations = DevicePlaceInputs.Check(args, display_properties);
        if (violations.Count > 0)
        {
            BridgeError error = ToolResults.InvalidArgumentError(violations);
            error.Execution = ExecutionState.NotStarted;
            return ToolResults.Error(error);
        }
        var request = new WriteRequest<DevicePlaceArgs, DevicePlaceArgs>(
            PlaceTool,
            ToolTier.Write,
            OperationNames.DevicePlacePlan,
            OperationNames.DevicePlaceExecute,
            args,
            plan => new DevicePlaceArgs
            {
                Project = args.Project,
                Page = args.Page,
                Part = args.Part,
                Variant = args.Variant,
                X = args.X,
                Y = args.Y,
                Tag = args.Tag,
                Library = args.Library,
                Symbol = args.Symbol,
                SymbolVariant = args.SymbolVariant,
                DisplayProperties = args.DisplayProperties,
                ConnectionDesignations = args.ConnectionDesignations,
                LocationBox = args.LocationBox,
                CornerX = args.CornerX,
                CornerY = args.CornerY,
                TerminalStrip = args.TerminalStrip,
                TerminalNumber = args.TerminalNumber,
                TerminalStripText = args.TerminalStripText,
                PlcConnectionPoint = args.PlcConnectionPoint,
                PlcAddress = args.PlcAddress,
                PlcSymbolicAddress = args.PlcSymbolicAddress,
                PlcText = args.PlcText,
                Fingerprint = plan.Fingerprint
            },
            dry_run,
            PageWriteTools.DefaultLimit,
            "Placing the device");
        PageDisplay display = await PageDisplay.ShowAsync(bridge, show_page, args.Project, args.Page, cancellationToken).ConfigureAwait(false);
        WriteOutcome<DevicePlaceData> outcome;
        try
        {
            outcome = await pipeline.RunAsync<DevicePlaceArgs, DevicePlaceArgs, DevicePlaceData>(request, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (BridgeCallException ex)
        {
            return display.Apply(ToolResults.Error(ex.Error));
        }

        bool committed = outcome.Execution == ExecutionState.Committed;
        Dictionary<string, string> details = outcome.Plan.Details ?? [];
        DevicePlaceData? data = outcome.Data;
        TerminalPlacementData? terminalData = data?.Terminal ?? (details.TryGetValue("terminal", out string? terminal) ? JsonSerializer.Deserialize<TerminalPlacementData>(terminal, HostJson.Options) : null);
        var result = new DevicePlaceResult
        {
            Project = ResultShaper.CleanText(data?.Project ?? outcome.Plan.Project),
            Execution = outcome.Execution,
            DryRun = dry_run,
            Page = ResultShaper.CleanText(data?.Page ?? outcome.Plan.Changes.FirstOrDefault()?.Name ?? args.Page),
            Part = ResultShaper.CleanText(data?.Part ?? details.GetValueOrDefault("part") ?? part ?? string.Empty),
            Variant = ResultShaper.CleanText(data?.Variant ?? details.GetValueOrDefault("variant") ?? string.Empty),
            Device = new DevicePlaceResult.DeviceEntry
            {
                Tag = ResultShaper.CleanText(data?.Tag ?? details.GetValueOrDefault("tag") ?? string.Empty),
                VisibleTag = ResultShaper.CleanText(data?.VisibleTag),
                Category = ResultShaper.CleanText(data?.Category),
            },
            Terminal = terminalData,
            Plc = data?.Plc,
            LocationBox = data?.LocationBox ?? (details.TryGetValue("locationBox", out string? box) ? JsonSerializer.Deserialize<LocationBoxData>(box, HostJson.Options) : null),
            Functions = data?.Functions ?? (args.LocationBox is not null ? 0 : terminalData is not null ? (terminalData.StripCreated ? 2 : 1) : args.Library is not null ? 1 : null),
            Unplaced = data?.Unplaced ?? (args.Library is not null || args.LocationBox is not null || args.TerminalStrip is not null ? 0 : null),
            Symbol = details.TryGetValue("symbol", out string? resolved) ? JsonSerializer.Deserialize<SymbolSearchItem>(resolved, HostJson.Options) : null,
            Changes = args.Library is not null || args.LocationBox is not null || args.TerminalStrip is not null ? [.. outcome.Plan.Changes.Select(c => new PlannedChange { Action = c.Action, Kind = c.Kind, Name = ResultShaper.CleanText(c.Name), Property = ResultShaper.CleanText(c.Property), NewValue = ResultShaper.CleanText(c.NewValue) })] : null,
            Undo = committed ? "AI: " + PlaceTool : null,
            Recovered = outcome.Recovered,
            Hint = PlaceHint(outcome, details),
        };
        if (result.Symbol is { } plannedSymbol)
        {
            plannedSymbol.Library = ResultShaper.CleanText(plannedSymbol.Library);
            plannedSymbol.Name = ResultShaper.CleanText(plannedSymbol.Name);
            plannedSymbol.Description = ResultShaper.CleanText(plannedSymbol.Description);
        }
        if (result.Terminal is { } placedTerminal)
        { placedTerminal.Strip = ResultShaper.CleanText(placedTerminal.Strip); placedTerminal.Owner = ResultShaper.CleanText(placedTerminal.Owner); placedTerminal.Number = ResultShaper.CleanText(placedTerminal.Number); placedTerminal.SortCode = ResultShaper.CleanText(placedTerminal.SortCode); }
        if (args.PlcConnectionPoint is not null)
        {
            result.Functions = null;
            result.Unplaced = null; // A55: implicit representations are verified after commit.
            result.Hint = "Verify committed PLC membership, calculated symbolic address and cardReference with eplan_plc_get response_format=detailed; unset references are not synthesized. " + result.Hint;
        }
        if (result.Plc is { } plc)
        {
            plc.Name = ResultShaper.CleanText(plc.Name);
            plc.Page = ResultShaper.CleanText(plc.Page);
            plc.Owner = ResultShaper.CleanText(plc.Owner);
            plc.Address = ResultShaper.CleanText(plc.Address);
            plc.ManualSymbolicAddress = ResultShaper.CleanText(plc.ManualSymbolicAddress);
            plc.SymbolicAddress = ResultShaper.CleanText(plc.SymbolicAddress);
            plc.Text = ResultShaper.CleanText(plc.Text);
            plc.CardReference = ResultShaper.CleanText(plc.CardReference);
            plc.DataType = ResultShaper.CleanText(plc.DataType);
        }
        if (result.LocationBox is { } location)
        { location.Structure = ResultShaper.CleanText(location.Structure); result.Hint = "Devices inside keep their own structure; affected devices: none. " + result.Hint; }
        string summary = args.LocationBox is not null ? (committed ? "Placed one dashed location box; affected devices: none. Ctrl+Z undoes this step." : "Dry run: would place one dashed location box; affected devices: none. Nothing was changed.") : !committed ? "Dry run: would place one device. Nothing was changed."
            : args.PlcConnectionPoint is not null ? "Placed one PLC point; verify it with eplan_plc_get. Ctrl+Z undoes this step."
            : data is null ? "Placed a device; Ctrl+Z in EPLAN undoes this step."
            : $"Placed a device with {ToolResults.Count(data.Functions, "function")}; Ctrl+Z in EPLAN undoes this step.";
        return display.Apply(ToolResults.Success(summary, result));
    }

    /// <summary>The hints of the tool spec (section 3), each when it applies, in one text.</summary>
    private static string? PlaceHint(WriteOutcome<DevicePlaceData> outcome, Dictionary<string, string> details)
    {
        DevicePlaceData? data = outcome.Data;
        var hints = new List<string>();
        if (outcome.Recovered)
        {
            hints.Add(details.ContainsKey("locationBox") ? "The response was lost, but EPLAN reported the box as placed; verify it with eplan_page_get and include_drawings." : "The response was lost, but EPLAN reported the device as placed; check it with eplan_device_get.");
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
